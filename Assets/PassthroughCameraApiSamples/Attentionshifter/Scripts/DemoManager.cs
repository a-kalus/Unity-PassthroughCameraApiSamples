using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the automatic target sequence:
///
///   SetupDialog (UserID + condition) -> Start pressed on the dialog ->
///   sequence starts (new CSV log session, bridge Init) -> target spawns
///   (bridge MoveLeft/MoveRight cue depending on the spawn side) -> user
///   points at it and presses the select button (see ControllerRayPointer)
///   -> target is destroyed -> random pause (range below) -> next target
///   spawns automatically, based on the head pose at that moment -> ...
///
/// The B button (configurable below) reopens the setup dialog while no
/// sequence is running, e.g. for the next participant. If no SetupDialog
/// exists in the scene, B starts the sequence directly instead.
/// </summary>
public class DemoManager : MonoBehaviour
{
    [SerializeField] private TargetSpawner spawner;

    [Tooltip("Optional. Auto-resolved if a ResponseTimeLogger exists in the scene. Logging is skipped when empty.")]
    [SerializeField] private ResponseTimeLogger logger;

    [Tooltip("Optional. Auto-resolved if a SetupDialog exists in the scene. With a dialog, B reopens it; without, B starts the sequence directly.")]
    [SerializeField] private SetupDialog setupDialog;

    [Tooltip("Optional. Auto-resolved if a USBStringSender exists in the scene. Init() is sent on sequence start; MoveLeft()/MoveRight() is sent at every target spawn depending on the side.")]
    [SerializeField] private USBStringSender usbSender;

    [Tooltip("Optional debug display. Auto-resolved if a DirectionDebugText exists in the scene; disable it with its own 'Show Debug Message' switch.")]
    [SerializeField] private DebugDirectionHint directionDebugText;

    [Header("Sequence")]
    [Tooltip("'Two' = B on the right controller. Reopens the setup dialog (or starts the sequence if no dialog exists). Ignored while a sequence is running.")]
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
        if (logger == null)
            logger = FindFirstObjectByType<ResponseTimeLogger>();
        if (setupDialog == null)
            setupDialog = FindFirstObjectByType<SetupDialog>();
        if (usbSender == null)
            usbSender = FindFirstObjectByType<USBStringSender>();
        if (directionDebugText == null)
            directionDebugText = FindFirstObjectByType<DebugDirectionHint>();
    }

    private void Update()
    {
        // Ignored while the sequence is running, so the button can't
        // accidentally despawn a target mid-run.
        if (SequenceRunning || !OVRInput.GetDown(startButton, OVRInput.Controller.RTouch))
            return;

        if (setupDialog != null)
        {
            if (!setupDialog.Visible)
                setupDialog.Show();
        }
        else
        {
            StartSequence();
        }
    }

    /// <summary>Starts the sequence and a new log session. Called by the SetupDialog's Start button.</summary>
    public void StartSequence()
    {
        if (SequenceRunning || spawner == null) return;

        if (logger != null)
            logger.StartSession();

        // Ping + Init the bridge so it accepts the movement cues that follow.
        if (usbSender != null)
            usbSender.Init();

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
        float precedingPause = 0f;   // the first target spawns immediately on start

        while (true)
        {
            // Head pose is sampled inside SpawnTarget* right now, i.e. AFTER the
            // pause below - so the angle is relative to the FOV at spawn time.
            GameObject target = spawner.SpawnTargetOutsideFov(minDegreesBeyondFov, maxAngle);
            if (target == null)
                yield break;

            // Directional bridge cue: the sign of the spawn azimuth is the side
            // (+ = right of the user, - = left of the user at spawn time).
            if (usbSender != null && spawner.LastSpawnAzimuth != 0f)
            {
                if (spawner.LastSpawnAzimuth < 0f)
                    usbSender.MoveLeft();
                else
                    usbSender.MoveRight();
            }

            // Debug display of the same direction (no-op while its switch is off).
            if (directionDebugText != null)
                directionDebugText.ShowForAzimuth(spawner.LastSpawnAzimuth);

            Target t = target.GetComponentInChildren<Target>();

            // Wait until the target is hit. At that moment its ReactionTime is
            // known but the component still exists (feedback delay), so we can
            // read and log it together with the pause that preceded this target.
            yield return new WaitUntil(() => t == null || t.IsHit);

            if (logger != null && t != null)
                logger.LogTarget(t.ReactionTime, precedingPause);

            // Now wait until the target GameObject has actually been destroyed
            // (hit + Target.destroyDelayAfterHit) - the pause starts here.
            yield return new WaitUntil(() => target == null);

            // Random inter-target pause; range is set in the inspector. This value
            // is logged as PrecedingPauseLength of the NEXT target.
            precedingPause = Random.Range(minRespawnDelay, maxRespawnDelay);
            yield return new WaitForSeconds(precedingPause);
        }
    }

    private void OnValidate()
    {
        minRespawnDelay = Mathf.Max(0f, minRespawnDelay);
        maxRespawnDelay = Mathf.Max(minRespawnDelay, maxRespawnDelay);
        minDegreesBeyondFov = Mathf.Max(0f, minDegreesBeyondFov);
    }
}
