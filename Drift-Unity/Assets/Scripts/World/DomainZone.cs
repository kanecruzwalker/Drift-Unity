using UnityEngine;

/// <summary>
/// DomainZone — client-side visual representation of a world zone.
///
/// Reads zoneStates[] from WorldManager each frame and drives:
///   · Fog of war mesh opacity (Undiscovered = fully opaque fog)
///   · Zone reveal animation (Discovered = fog lifts)
///   · Hazard visual overlay (Contested/Undiscovered in PvE = red tint)
///   · Minimap dot color (driven by ZoneState)
///   · Station pulse visual (Safe = teal glow)
///
/// NEVER writes to the network — purely cosmetic, client-local (ADR-014).
/// Each client runs this independently. Fog state is per-client.
/// Zone UNLOCK state (ZoneState changes) is synced via WorldManager NetworkVariable.
///
/// Attach to: one DomainZone GameObject per zone, child of WorldManager.
/// ZoneIndex must match the index in WorldManager.zoneStates[].
/// </summary>
public class DomainZone : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Zone Identity")]
    [Tooltip("Index into WorldManager.zoneStates[] — must match server assignment.")]
    [SerializeField] public int zoneIndex = 0;

    [Header("Fog of War")]
    [Tooltip("Renderer for the fog overlay mesh. Alpha driven by zone state.")]
    [SerializeField] private Renderer fogRenderer;

    [Tooltip("Fog alpha when fully undiscovered.")]
    [SerializeField] private float fogAlphaUndiscovered = 0.92f;

    [Tooltip("Fog alpha when discovered but not safe.")]
    [SerializeField] private float fogAlphaDiscovered = 0.0f;

    [Tooltip("Speed at which fog fades in/out on state change.")]
    [SerializeField] private float fogFadeSpeed = 2f;

    [Header("Hazard Overlay")]
    [Tooltip("Renderer for the hazard danger tint overlay (PvE mode).")]
    [SerializeField] private Renderer hazardRenderer;

    [Tooltip("Hazard overlay alpha in Undiscovered zones.")]
    [SerializeField] private float hazardAlphaUndiscovered = 0.4f;

    [Tooltip("Hazard overlay alpha in Contested zones.")]
    [SerializeField] private float hazardAlphaContested = 0.25f;

    [Header("Ground Tint")]
    [Tooltip("Renderer for the zone ground plane — tinted by zone state.")]
    [SerializeField] private Renderer groundRenderer;

    // ─────────────────────────────────────────────────────────────────────────
    // ZONE STATE COLORS — bioluminescent space palette
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Color GroundUndiscovered = new Color(0.05f, 0.04f, 0.08f);
    private static readonly Color GroundDiscovered = new Color(0.08f, 0.07f, 0.12f);
    private static readonly Color GroundContested = new Color(0.15f, 0.08f, 0.05f);
    private static readonly Color GroundSafe = new Color(0.05f, 0.12f, 0.12f);

    /// Minimap dot color for this zone — read by MiniMap.cs in feature/hud-and-polish.
    public Color MinimapColor { get; private set; } = Color.gray;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    private ZoneState _currentState = ZoneState.Undiscovered;
    private ZoneState _previousState = ZoneState.Undiscovered;
    private float _fogAlphaCurrent = 0.92f;
    private bool _revealAnimating = false;
    private float _revealTimer = 0f;
    private const float RevealDuration = 1.5f;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        ApplyStateImmediate(ZoneState.Undiscovered);
    }

    private void Update()
    {
        PollZoneState();
        UpdateFogFade();
        UpdateRevealAnimation();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // STATE POLLING
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls WorldManager for the current zone state each frame.
    /// When the state changes, triggers the appropriate visual transition.
    /// Client-local — never writes back to the network.
    /// </summary>
    private void PollZoneState()
    {
        if (WorldManager.Instance == null) return;

        ZoneState newState = WorldManager.Instance.GetZoneState(zoneIndex);
        if (newState == _currentState) return;

        _previousState = _currentState;
        _currentState = newState;

        OnZoneStateChanged(_previousState, _currentState);
    }

    /// <summary>
    /// Responds to a zone state change with appropriate visual transition.
    /// Called when PollZoneState detects a difference.
    /// </summary>
    private void OnZoneStateChanged(ZoneState previous, ZoneState next)
    {
        Debug.Log($"[DomainZone {zoneIndex}] {previous} → {next}");

        switch (next)
        {
            case ZoneState.Discovered:
                StartRevealAnimation();
                break;

            case ZoneState.Contested:
                SetGroundColor(GroundContested);
                SetHazardAlpha(hazardAlphaContested);
                UpdateMinimapColor(next);
                break;

            case ZoneState.Safe:
                SetGroundColor(GroundSafe);
                SetFogAlpha(0f);
                SetHazardAlpha(0f);
                UpdateMinimapColor(next);
                break;

            case ZoneState.Undiscovered:
                ApplyStateImmediate(ZoneState.Undiscovered);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOG OF WAR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Smoothly fades fog alpha toward the target value each frame.
    /// Driven by fogFadeSpeed — gradual for natural feel.
    /// </summary>
    private void UpdateFogFade()
    {
        if (fogRenderer == null) return;

        float targetAlpha = _currentState == ZoneState.Undiscovered
            ? fogAlphaUndiscovered
            : fogAlphaDiscovered;

        _fogAlphaCurrent = Mathf.MoveTowards(
            _fogAlphaCurrent, targetAlpha,
            fogFadeSpeed * Time.deltaTime);

        Color c = fogRenderer.material.color;
        c.a = _fogAlphaCurrent;
        fogRenderer.material.color = c;
    }

    private void SetFogAlpha(float alpha)
    {
        _fogAlphaCurrent = alpha;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REVEAL ANIMATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers the zone reveal animation — fog lifts, ground brightens.
    /// Called when zone transitions from Undiscovered → Discovered.
    /// Animation runs over RevealDuration seconds.
    /// </summary>
    private void StartRevealAnimation()
    {
        _revealAnimating = true;
        _revealTimer = 0f;
        UpdateMinimapColor(ZoneState.Discovered);
    }

    private void UpdateRevealAnimation()
    {
        if (!_revealAnimating) return;

        _revealTimer += Time.deltaTime;
        float t = Mathf.Clamp01(_revealTimer / RevealDuration);

        // Lerp ground from undiscovered dark to discovered color.
        if (groundRenderer != null)
        {
            groundRenderer.material.color = Color.Lerp(
                GroundUndiscovered, GroundDiscovered, t);
        }

        if (t >= 1f)
        {
            _revealAnimating = false;
            SetGroundColor(GroundDiscovered);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VISUAL HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyStateImmediate(ZoneState state)
    {
        switch (state)
        {
            case ZoneState.Undiscovered:
                SetFogAlpha(fogAlphaUndiscovered);
                SetGroundColor(GroundUndiscovered);
                SetHazardAlpha(hazardAlphaUndiscovered);
                UpdateMinimapColor(state);
                break;
            case ZoneState.Safe:
                SetFogAlpha(0f);
                SetGroundColor(GroundSafe);
                SetHazardAlpha(0f);
                UpdateMinimapColor(state);
                break;
        }
    }

    private void SetGroundColor(Color color)
    {
        if (groundRenderer == null) return;
        groundRenderer.material.color = color;
        if (groundRenderer.material.HasProperty("_EmissionColor"))
        {
            groundRenderer.material.EnableKeyword("_EMISSION");
            groundRenderer.material.SetColor("_EmissionColor", color * 0.5f);
        }
    }

    private void SetHazardAlpha(float alpha)
    {
        if (hazardRenderer == null) return;
        Color c = hazardRenderer.material.color;
        c.a = alpha;
        hazardRenderer.material.color = c;
    }

    private void UpdateMinimapColor(ZoneState state)
    {
        MinimapColor = state switch
        {
            ZoneState.Undiscovered => new Color(0.1f, 0.1f, 0.15f),
            ZoneState.Discovered => new Color(0.3f, 0.3f, 0.5f),
            ZoneState.Contested => new Color(1.0f, 0.5f, 0.0f),
            ZoneState.Safe => new Color(0.25f, 0.88f, 0.82f),
            _ => Color.gray
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC API — called by WorldManager and MiniMap
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current ZoneState for this zone.
    /// Read by MiniMap.cs to color the zone dot.
    /// </summary>
    public ZoneState GetCurrentState() => _currentState;
}