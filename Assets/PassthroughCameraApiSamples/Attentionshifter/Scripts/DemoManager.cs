using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the automatic target sequence, started with the B button:
///
///   press B (right controller) -> sequence starts -> target spawns ->
///   user points at it and presses the select button (see ControllerRayPointer)
///   -> target is destroyed -> random pause (range below) -> next target
///   spawns automatically, based on the head pose at that moment -> ...
///
/// Starting via button press (instead of on scene load) guarantees that head
/// tracking is already running, so the first target uses a valid head pose
/// and no longer spawns at floor level.
/// The pause is measured from the actual destruction of the target GameObject
/// (i.e. hit + Target.destroyDelayAfterHit).
/// </summary>
public class DemoManager : MonoBehaviour
{
    [SerializeField] private TargetSpawner spawner;

    [Header("Sequence")]
    [Tooltip("Button that starts the sequence. 'Two' = B on the right controller. Presses while the sequence is already running are ignored.")]
    [SerializeField] private OVRInput.Button startButton = OVRInput.Button.Two;

    [Tooltip("Minimum pause between destruction of one target and spawn of the next, in seconds.")]
    [SerializeField] private float minRespawnDelay = 1f;

    [Tooltip("Maximum pause between destruction of one target and spawn of the next, in seconds.")]
    [SerializeField] private float maxRespawnDelay = 3f;

    [Header("Angles")]
    [Tooltip("How many degrees beyond the FOV edge a random target must at least be.")]
    [SerializeField] private float minDegreesBeyondFov = 5f;

    [Tooltip("Maximum absolute random angle, in degrees (180 = directly behind).")]
    [SerializeField] private float maxAngle = 180f;

    /// <summary>True while the automatic sequence is running.</summary>
    public bool SequenceRunning => sequenceRoutine != null;

    private Coroutine sequenceRoutine;

    private void Awake()
    {
        if (spawner == null)
            spawner = FindFirstObjectByType<TargetSpawner>();
    }

    private void Update()
    {
        // B starts the sequence. While it is running the button does nothing,
        // so it can't accidentally despawn a target mid-run.
        if (!SequenceRunning && OVRInput.GetDown(startButton, OVRInput.Controller.RTouch))
            StartSequence();
    }

    /// <summary>Starts the sequence. Also callable from your own code or UnityEvents.</summary>
    public void StartSequence()
    {
        if (SequenceRunning || spawner == null) return;
        sequenceRoutine = StartCoroutine(SequenceLoop());
    }

    /// <summary>Stops the sequence and removes the current target.</summary>
    public void StopSequence()
    {
        if (sequenceRoutine != null)
            StopCoroutine(sequenceRoutine);
        sequenceRoutine = null;

        if (spawner != null)
            spawner.DespawnCurrentTarget();
    }

    private IEnumerator SequenceLoop()
    {
        while (true)
        {
            // Head pose is sampled inside SpawnTarget* right now, i.e. AFTER the
            // pause below - so the angle is relative to the FOV at spawn time.
            GameObject target = spawner.SpawnTargetOutsideFov(minDegreesBeyondFov, maxAngle);
            if (target == null)
                yield break;

            // Wait until the target GameObject has actually been destroyed.
            // Only the ray pointer can do that: point at the target + select button.
            yield return new WaitUntil(() => target == null);

            // Random inter-target pause; range is set in the inspector.
            // The next target then spawns automatically at the top of the loop.
            yield return new WaitForSeconds(Random.Range(minRespawnDelay, maxRespawnDelay));
        }
    }

    private void OnValidate()
    {
        minRespawnDelay = Mathf.Max(0f, minRespawnDelay);
        maxRespawnDelay = Mathf.Max(minRespawnDelay, maxRespawnDelay);
        minDegreesBeyondFov = Mathf.Max(0f, minDegreesBeyondFov);
    }
}

