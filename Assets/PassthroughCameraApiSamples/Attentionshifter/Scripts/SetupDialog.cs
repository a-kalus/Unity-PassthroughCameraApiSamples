using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// In-headset setup dialog shown before the sequence starts: set the UserID
/// (prefix + number via the +/- buttons), pick one of two conditions, press
/// Start. Built procedurally at runtime from flattened cubes and 3D text and
/// operated with the controller ray + select button - no Canvas, EventSystem
/// or Interaction SDK required.
///
/// Flow: the dialog appears automatically once head tracking is valid.
/// Start writes UserID + condition into the ResponseTimeLogger, hides the
/// dialog and starts the DemoManager sequence. Pressing B later (while no
/// sequence is running) reopens the dialog for the next participant.
/// </summary>
public class SetupDialog : MonoBehaviour
{
    [Header("References (auto-resolved if empty)")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private DemoManager demoManager;
    [SerializeField] private ResponseTimeLogger logger;

    [Header("Input")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
    [Tooltip("Button that clicks dialog buttons. Keep identical to the pointer's select button.")]
    [SerializeField] private OVRInput.Button selectButton = OVRInput.Button.One;

    [Header("User ID")]
    [Tooltip("Shown in front of the number, e.g. 'P' -> P01, P02, ...")]
    [SerializeField] private string userIdPrefix = "P";
    [SerializeField] private int userNumber = 1;
    [SerializeField] private int minUserNumber = 0;
    [SerializeField] private int maxUserNumber = 999;

    [Header("Conditions")]
    [Tooltip("Labels of the two condition buttons. The selected label is written into the CSV.")]
    [SerializeField] private string conditionAName = "Condition A";
    [SerializeField] private string conditionBName = "Condition B";

    [Header("Placement")]
    [Tooltip("Distance of the dialog from the head, in meters.")]
    [SerializeField] private float distance = 1.2f;

    /// <summary>Current UserID, e.g. "P07".</summary>
    public string UserId => userIdPrefix + userNumber.ToString("D2");

    /// <summary>Label of the currently selected condition.</summary>
    public string SelectedCondition { get; private set; }

    public bool Visible => panelRoot != null && panelRoot.activeSelf;

    private GameObject panelRoot;
    private TextMesh userIdText;
    private SetupDialogButton conditionAButton;
    private SetupDialogButton conditionBButton;
    private SetupDialogButton hovered;
    private Font font;

    private void Awake()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;

        if (rayOrigin == null)
        {
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
                rayOrigin = controller == OVRInput.Controller.LTouch
                    ? rig.leftControllerAnchor
                    : rig.rightControllerAnchor;
        }

        if (demoManager == null) demoManager = FindFirstObjectByType<DemoManager>();
        if (logger == null) logger = FindFirstObjectByType<ResponseTimeLogger>();

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        SelectedCondition = conditionAName;

        BuildDialog();
        panelRoot.SetActive(false);
    }

    private IEnumerator Start()
    {
        // Wait until head tracking delivers a real pose (camera leaves floor
        // level), so the dialog is not placed at the ground. 3 s fallback for
        // running without a headset (e.g. in the editor).
        float waited = 0f;
        while (head != null && head.position.y < 0.2f && waited < 3f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        Show();
    }

    private void Update()
    {
        if (!Visible || rayOrigin == null) return;

        // Find the closest dialog button along the controller ray.
        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, 10f);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        SetupDialogButton button = null;
        foreach (RaycastHit hit in hits)
        {
            SetupDialogButton b = hit.collider.GetComponentInParent<SetupDialogButton>();
            if (b != null) { button = b; break; }
        }

        if (button != hovered)
        {
            if (hovered != null) hovered.SetHovered(false);
            hovered = button;
            if (hovered != null) hovered.SetHovered(true);
        }

        if (hovered != null && OVRInput.GetDown(selectButton, controller))
            hovered.Click();
    }

    /// <summary>Places the dialog in front of the user's current view and shows it.</summary>
    public void Show()
    {
        if (head != null)
        {
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
            flat.Normalize();

            panelRoot.transform.SetPositionAndRotation(
                head.position + flat * distance,
                Quaternion.LookRotation(flat, Vector3.up));
        }

        RefreshUserIdText();
        panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (hovered != null) hovered.SetHovered(false);
        hovered = null;
        panelRoot.SetActive(false);
    }

    // ---------------------------------------------------------------- actions

    private void ChangeUserNumber(int delta)
    {
        userNumber = Mathf.Clamp(userNumber + delta, minUserNumber, maxUserNumber);
        RefreshUserIdText();
    }

    private void SelectCondition(bool a)
    {
        SelectedCondition = a ? conditionAName : conditionBName;
        conditionAButton.SetSelected(a);
        conditionBButton.SetSelected(!a);
    }

    private void OnStartClicked()
    {
        if (logger != null)
            logger.SetSession(UserId, SelectedCondition);

        Hide();

        if (demoManager != null)
            demoManager.StartSequence();
    }

    private void RefreshUserIdText()
    {
        if (userIdText != null)
            userIdText.text = "User: " + UserId;
    }

    // ----------------------------------------------------------- construction

    private void BuildDialog()
    {
        panelRoot = new GameObject("SetupDialogPanel");
        panelRoot.transform.SetParent(transform, false);

        // Background (keeps its BoxCollider so the visible pointer ray ends on it).
        CreateBox(Vector3.zero, new Vector3(0.70f, 0.46f, 0.01f), new Color(0.12f, 0.12f, 0.14f));

        CreateText(new Vector3(0f, 0.17f, -0.02f), "Session Setup", 0.011f, FontStyle.Bold);

        userIdText = CreateText(new Vector3(0f, 0.07f, -0.02f), "User: " + UserId, 0.011f, FontStyle.Bold);
        CreateButton("-", new Vector2(-0.22f, 0.07f), new Vector2(0.09f, 0.075f), 0.012f,
                     () => ChangeUserNumber(-1));
        CreateButton("+", new Vector2(0.22f, 0.07f), new Vector2(0.09f, 0.075f), 0.012f,
                     () => ChangeUserNumber(+1));

        conditionAButton = CreateButton(conditionAName, new Vector2(-0.16f, -0.045f), new Vector2(0.28f, 0.08f), 0.0065f,
                                        () => SelectCondition(true));
        conditionBButton = CreateButton(conditionBName, new Vector2(0.16f, -0.045f), new Vector2(0.28f, 0.08f), 0.0065f,
                                        () => SelectCondition(false));
        conditionAButton.SetSelected(true);

        CreateButton("Start", new Vector2(0f, -0.16f), new Vector2(0.26f, 0.09f), 0.009f,
                     OnStartClicked, new Color(0.16f, 0.50f, 0.28f));
    }

    private void CreateBox(Vector3 localPos, Vector3 size, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Panel";
        go.transform.SetParent(panelRoot.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().material.color = color;
    }

    private SetupDialogButton CreateButton(string label, Vector2 pos, Vector2 size, float labelCharSize,
                                           Action onClick, Color? idleColor = null)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // has a BoxCollider
        go.name = "Button_" + label;
        go.transform.SetParent(panelRoot.transform, false);
        go.transform.localPosition = new Vector3(pos.x, pos.y, -0.012f);
        go.transform.localScale = new Vector3(size.x, size.y, 0.012f);

        SetupDialogButton button = go.AddComponent<SetupDialogButton>();
        button.Init(go.GetComponent<Renderer>(),
                    idleColor ?? new Color(0.32f, 0.34f, 0.38f),
                    new Color(0.15f, 0.45f, 0.85f),
                    onClick);

        // Label is a sibling (not a child) so the cube's non-uniform scale
        // doesn't distort the text.
        CreateText(new Vector3(pos.x, pos.y, -0.022f), label, labelCharSize);
        return button;
    }

    private TextMesh CreateText(Vector3 localPos, string text, float characterSize,
                                FontStyle style = FontStyle.Normal)
    {
        GameObject go = new GameObject("Text_" + text);
        go.transform.SetParent(panelRoot.transform, false);
        go.transform.localPosition = localPos;

        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 60;
        tm.characterSize = characterSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = style;

        if (font != null)
        {
            tm.font = font;
            go.GetComponent<MeshRenderer>().material = font.material;
        }
        return tm;
    }
}

/// <summary>
/// One clickable dialog button: background color reflects idle / selected /
/// hovered state; Click() invokes the assigned action. Added at runtime by
/// SetupDialog - not meant to be used from the inspector.
/// </summary>
public class SetupDialogButton : MonoBehaviour
{
    private Renderer background;
    private Color idleColor;
    private Color selectedColor;
    private Action onClick;
    private bool selected;
    private bool hovered;

    public void Init(Renderer bg, Color idle, Color selectedCol, Action clickAction)
    {
        background = bg;
        idleColor = idle;
        selectedColor = selectedCol;
        onClick = clickAction;
        Refresh();
    }

    public void SetHovered(bool value) { hovered = value; Refresh(); }
    public void SetSelected(bool value) { selected = value; Refresh(); }
    public void Click() => onClick?.Invoke();

    private void Refresh()
    {
        if (background == null) return;
        Color c = selected ? selectedColor : idleColor;
        if (hovered) c = Color.Lerp(c, Color.white, 0.35f);
        background.material.color = c;
    }
}
