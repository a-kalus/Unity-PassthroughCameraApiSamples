using UnityEngine;

/// <summary>
/// Spawns targets at a given angle relative to the center of the user's
/// current field of view (i.e. the head's gaze direction at spawn time).
///
/// Angle convention (azimuth):
///     0°  = center of the current FOV (straight ahead)
///   +90°  = 90° to the right,  -90° = 90° to the left,  ±180° = behind
///
/// Attach to any GameObject in a scene that contains an OVRCameraRig.
/// </summary>
public class TargetSpawner : MonoBehaviour
{
    [Header("References")]
    [Tooltip("CenterEyeAnchor of the OVRCameraRig. Falls back to Camera.main if left empty.")]
    [SerializeField] private Transform head;

    [Tooltip("Optional target prefab (should have a Collider; a Target component is added automatically if missing). If left empty, a 25 cm sphere is created at runtime.")]
    [SerializeField] private GameObject targetPrefab;

    [Header("Spawn Settings")]
    [Tooltip("Distance from the head to the spawned target, in meters.")]
    [SerializeField] private float spawnDistance = 2.0f;

    [Tooltip("Half of the device's horizontal FOV in degrees (Quest 3 ≈ 55°). Used only to warn when a requested angle would land inside the visible FOV.")]
    [SerializeField] private float halfHorizontalFovDeg = 55f;

    [Tooltip("If true, the azimuth is measured in the world's horizontal plane and the target spawns at head height (head pitch/roll are ignored). If false, angles are relative to the full head orientation.")]
    [SerializeField] private bool useHorizontalPlane = true;

    /// <summary>The most recently spawned target that still exists (or null).</summary>
    public GameObject CurrentTarget { get; private set; }

    private void Awake()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;
    }

    /// <summary>
    /// Spawns a target at <paramref name="angleDeg"/> degrees horizontally away from
    /// the center of the current field of view (positive = right, negative = left).
    /// </summary>
    public GameObject SpawnTarget(float angleDeg)
    {
        return SpawnTarget(angleDeg, 0f);
    }

    /// <summary>
    /// Spawns a target at the given azimuth (horizontal, + = right) and elevation
    /// (vertical, + = up) relative to the center of the current field of view.
    /// </summary>
    public GameObject SpawnTarget(float azimuthDeg, float elevationDeg)
    {
        if (head == null)
        {
            Debug.LogError("[TargetSpawner] No head transform assigned and no camera tagged 'MainCamera' found.");
            return null;
        }

        if (!IsOutsideFieldOfView(azimuthDeg))
            Debug.LogWarning($"[TargetSpawner] |{azimuthDeg:F0}°| <= {halfHorizontalFovDeg:F0}° - the target may spawn inside the current FOV.");

        // Reference frame = current gaze direction (yaw only, if useHorizontalPlane).
        Quaternion gazeReference = GetGazeReference();

        // Pitch by the elevation, then yaw by the azimuth, relative to the gaze.
        Vector3 direction = gazeReference * Quaternion.Euler(-elevationDeg, azimuthDeg, 0f) * Vector3.forward;
        Vector3 position = head.position + direction * spawnDistance;
        Quaternion rotation = Quaternion.LookRotation(-direction, Vector3.up); // +Z of the target faces the user

        DespawnCurrentTarget();
        CurrentTarget = CreateTargetInstance(position, rotation);
        return CurrentTarget;
    }

    /// <summary>Spawns a target on a random side at a random angle guaranteed to be outside the FOV.</summary>
    public GameObject SpawnTargetOutsideFov(float minDegreesBeyondFov = 5f, float maxAngle = 180f)
    {
        float min = halfHorizontalFovDeg + minDegreesBeyondFov;
        float angle = Random.Range(min, Mathf.Max(min, maxAngle));
        if (Random.value < 0.5f) angle = -angle;
        return SpawnTarget(angle);
    }

    /// <summary>True if the given azimuth lies outside the (approximate) horizontal FOV.</summary>
    public bool IsOutsideFieldOfView(float azimuthDeg)
    {
        return Mathf.Abs(azimuthDeg) > halfHorizontalFovDeg;
    }

    public void DespawnCurrentTarget()
    {
        if (CurrentTarget != null)
            Destroy(CurrentTarget);
        CurrentTarget = null;
    }

    private Quaternion GetGazeReference()
    {
        if (!useHorizontalPlane)
            return head.rotation;

        Vector3 flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-4f)                        // looking straight up/down
            flatForward = Vector3.ProjectOnPlane(head.up, Vector3.up);

        return Quaternion.LookRotation(flatForward.normalized, Vector3.up);
    }

    private GameObject CreateTargetInstance(Vector3 position, Quaternion rotation)
    {
        GameObject go;
        if (targetPrefab != null)
        {
            go = Instantiate(targetPrefab, position, rotation);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);    // comes with a SphereCollider
            go.name = "Target";
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = Vector3.one * 0.25f;
        }

        if (go.GetComponentInChildren<Target>() == null)
            go.AddComponent<Target>();

        return go;
    }
}
