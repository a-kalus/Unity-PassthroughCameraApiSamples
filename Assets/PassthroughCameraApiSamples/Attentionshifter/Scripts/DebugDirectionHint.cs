using System.Collections;
using UnityEngine;

/// <summary>
/// Debug helper: briefly shows "LEFT" or "RIGHT" in front of the user whenever
/// a target spawns, so the direction cue sent to the bridge can be verified
/// without looking at the log.
///
/// Uncheck 'Show Debug Message' in the inspector to disable it completely -
/// nothing is created or displayed then, and no other script needs changing.
///
/// The text is a 3D TextMesh placed in front of the current view; it does not
/// need a Canvas and never blocks the controller ray (no collider).
/// </summary>
public class DebugDirectionHint : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("Master switch: uncheck to disable the on-screen direction message.")]
    [SerializeField] private bool showDebugMessage = true;

    [Header("Appearance")]
    [Tooltip("How long the message stays visible, in seconds.")]
    [SerializeField] private float displayDuration = 1.0f;

    [Tooltip("Distance of the message from the head, in meters.")]
    [SerializeField] private float distance = 1.5f;

    [Tooltip("Vertical offset relative to eye height, in meters (negative = below the view center).")]
    [SerializeField] private float verticalOffset = -0.25f;

    [SerializeField] private Color leftColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField] private Color rightColor = new Color(0.3f, 0.7f, 1f);

    [Tooltip("Keep the message facing the user while it is visible.")]
    [SerializeField] private bool followHead = true;

    [Header("References (auto-resolved if empty)")]
    [SerializeField] private Transform head;

    private TextMesh textMesh;
    private Coroutine hideRoutine;

    private void Awake()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;
    }

    /// <summary>Shows "LEFT" or "RIGHT" depending on the sign of the spawn azimuth.</summary>
    public void ShowForAzimuth(float azimuthDeg)
    {
        if (azimuthDeg < 0f) ShowLeft();
        else ShowRight();
    }

    public void ShowLeft() => Show("LEFT", leftColor);
    public void ShowRight() => Show("RIGHT", rightColor);

    /// <summary>Shows any message. Does nothing while the debug switch is off.</summary>
    public void Show(string message, Color color)
    {
        if (!showDebugMessage) return;

        EnsureTextMesh();

        textMesh.text = message;
        textMesh.color = color;
        textMesh.gameObject.SetActive(true);
        PlaceInFrontOfHead();

        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    public void Hide()
    {
        if (hideRoutine != null) { StopCoroutine(hideRoutine); hideRoutine = null; }
        if (textMesh != null) textMesh.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!followHead || textMesh == null || !textMesh.gameObject.activeSelf) return;
        PlaceInFrontOfHead();
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(displayDuration);
        if (textMesh != null) textMesh.gameObject.SetActive(false);
        hideRoutine = null;
    }

    private void PlaceInFrontOfHead()
    {
        if (head == null || textMesh == null) return;

        Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.forward;
        flat.Normalize();

        textMesh.transform.SetPositionAndRotation(
            head.position + flat * distance + Vector3.up * verticalOffset,
            Quaternion.LookRotation(flat, Vector3.up));
    }

    private void EnsureTextMesh()
    {
        if (textMesh != null) return;

        GameObject go = new GameObject("DirectionDebugText");
        go.transform.SetParent(transform, false);

        textMesh = go.AddComponent<TextMesh>();
        textMesh.text = "";
        textMesh.fontSize = 60;
        textMesh.characterSize = 0.02f;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontStyle = FontStyle.Bold;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            textMesh.font = font;
            go.GetComponent<MeshRenderer>().material = font.material;
        }

        go.SetActive(false);
    }

    private void OnValidate()
    {
        displayDuration = Mathf.Max(0.1f, displayDuration);

        // Let the switch take effect immediately, also during play mode.
        if (!showDebugMessage && textMesh != null)
            textMesh.gameObject.SetActive(false);
    }
}
