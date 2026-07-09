using UnityEngine;

/// <summary>
/// Behaviour of a single target: highlight while the controller ray points at it,
/// feedback + despawn when hit. Needs a Collider on the same object (one is
/// added automatically if missing).
/// </summary>
public class Target : MonoBehaviour
{
    [SerializeField] private Color idleColor = Color.red;
    [SerializeField] private Color hoverColor = Color.yellow;
    [SerializeField] private Color hitColor = Color.green;

    [Tooltip("Seconds the target stays visible after being hit before it is destroyed.")]
    [SerializeField] private float destroyDelayAfterHit = 0.3f;

    /// <summary>Time.time at which this target was spawned.</summary>
    public float SpawnTime { get; private set; }

    /// <summary>Seconds between spawn and hit (0 while not yet hit).</summary>
    public float ReactionTime { get; private set; }

    public bool IsHit { get; private set; }

    private Renderer targetRenderer;

    private void Awake()
    {
        SpawnTime = Time.time;
        targetRenderer = GetComponentInChildren<Renderer>();

        if (GetComponentInChildren<Collider>() == null)
            gameObject.AddComponent<SphereCollider>();

        SetColor(idleColor);
    }

    /// <summary>Called by the pointer when the ray enters/leaves this target.</summary>
    public void SetHovered(bool hovered)
    {
        if (IsHit) return;
        SetColor(hovered ? hoverColor : idleColor);
    }

    /// <summary>Registers a hit (color feedback, reaction time, delayed despawn).</summary>
    public void Hit()
    {
        if (IsHit) return;
        IsHit = true;

        ReactionTime = Time.time - SpawnTime;
        Debug.Log($"[Target] Hit after {ReactionTime:F2} s");

        SetColor(hitColor);
        Destroy(gameObject, destroyDelayAfterHit);
    }

    private void SetColor(Color color)
    {
        if (targetRenderer != null)
            targetRenderer.material.color = color;
    }
}
