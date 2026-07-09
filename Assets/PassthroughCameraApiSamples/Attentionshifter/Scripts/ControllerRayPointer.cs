using System;
using UnityEngine;
using UnityEngine.Events;
 
/// <summary>
/// Casts a ray from a controller, highlights the Target it points at and
/// confirms a hit with the select button (default: A) - but only while the
/// ray is actually on the target. Pressing the button while not pointing at
/// a target does nothing.
/// Attach to any GameObject; references resolve automatically from the OVRCameraRig.
/// </summary>
public class ControllerRayPointer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Controller anchor used as ray origin (e.g. RightControllerAnchor). Auto-resolved from the OVRCameraRig if left empty.")]
    [SerializeField] private Transform rayOrigin;

    [Tooltip("Controller whose button confirms a hit.")]
    [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Tooltip("Button that confirms a hit while pointing at a target. 'One' = A on the right controller; use 'PrimaryIndexTrigger' for the trigger instead.")]
    [SerializeField] private OVRInput.Button selectButton = OVRInput.Button.One;

    [Tooltip("Optional. A simple cyan line is created automatically if left empty.")]
    [SerializeField] private LineRenderer lineRenderer;

    [Header("Ray Settings")]
    [SerializeField] private float maxRayLength = 20f;
    [SerializeField] private LayerMask layerMask = ~0;

    [Header("Selection")]
    [Tooltip("If > 0: pointing at a target for this many seconds counts as a hit, no button press needed.")]
    [SerializeField] private float dwellTime = 0f;

    [Header("Events")]
    public UnityEvent<Target> onTargetHit;

    private Target hovered;
    private float hoverTimer;

    private void Awake()
    {
        if (rayOrigin == null)
        {
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
                rayOrigin = controller == OVRInput.Controller.LTouch
                    ? rig.leftControllerAnchor
                    : rig.rightControllerAnchor;
        }

        if (lineRenderer == null)
            lineRenderer = CreateDefaultLineRenderer();
    }

    private void Update()
    {
        if (rayOrigin == null) return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        Vector3 rayEnd = ray.origin + ray.direction * maxRayLength;

        // Find the closest Target along the ray. Non-target colliders (e.g. the
        // room/scene mesh) only shorten the visual ray - they don't block selection.
        Target target = null;
        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayLength, layerMask);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            Target t = hit.collider.GetComponentInParent<Target>();
            if (t != null)
            {
                target = t;
                rayEnd = hit.point;
                break;
            }
        }
        if (target == null && hits.Length > 0)
            rayEnd = hits[0].point;

        UpdateHover(target);
        DrawLine(ray.origin, rayEnd);

        // A hit requires BOTH: the ray is on a target AND the select button is
        // pressed (or the optional dwell time has elapsed). Button presses while
        // not pointing at a target are ignored.
        bool selectPressed = OVRInput.GetDown(selectButton, controller);
        bool dwellCompleted = dwellTime > 0f && hovered != null && hoverTimer >= dwellTime;

        if (hovered != null && (selectPressed || dwellCompleted))
        {
            Target hitTarget = hovered;
            UpdateHover(null);
            hitTarget.Hit();
            onTargetHit?.Invoke(hitTarget);
        }
    }

    private void UpdateHover(Target target)
    {
        if (target == hovered)
        {
            if (hovered != null)
                hoverTimer += Time.deltaTime;
            return;
        }

        if (hovered != null) hovered.SetHovered(false);
        hovered = target;
        hoverTimer = 0f;
        if (hovered != null) hovered.SetHovered(true);
    }

    private void DrawLine(Vector3 start, Vector3 end)
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
    }

    private LineRenderer CreateDefaultLineRenderer()
    {
        LineRenderer lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.startWidth = lr.endWidth = 0.005f;
        lr.startColor = lr.endColor = Color.cyan;
        lr.positionCount = 2;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
            lr.material = new Material(shader);

        return lr;
    }
}
