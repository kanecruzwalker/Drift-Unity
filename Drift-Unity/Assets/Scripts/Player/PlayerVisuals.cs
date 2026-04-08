using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PlayerVisuals — cosmetic visual layer for the player entity.
///
/// Driven entirely by PlayerController and NetworkPlayer — no game logic here.
/// All effects are purely cosmetic and run on every client (not just owner).
///
/// Responsibilities:
///   · Trail renderer — active during movement, color matches player color
///   · Damage flash   — brief white flash when health decreases
///   · Death effect   — fade out on death
///   · Dash burst     — scale pulse on double-tap dash
///   · Color sync     — reads playerColor NetworkVariable and applies to materials
///
/// Aesthetic: deep space / enchanted forest hybrid.
///   Player is a soft amorphous light blob — glowing sphere with pulsing aura.
///   Color assigned by golden-angle HSL distribution (same as PlayAI balloons).
///
/// Attach to: the Player prefab root alongside PlayerController.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class PlayerVisuals : NetworkBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Trail renderer for movement — assign the child TrailRenderer.")]
    [SerializeField] private TrailRenderer trailRenderer;

    [Header("Pulse Settings")]
    [Tooltip("Base scale of the player mesh.")]
    [SerializeField] private float baseScale = 1f;

    [Tooltip("Scale multiplier applied on dash burst.")]
    [SerializeField] private float dashScalePulse = 1.4f;

    [Tooltip("Duration of the dash scale pulse in seconds.")]
    [SerializeField] private float dashPulseDuration = 0.25f;

    [Tooltip("Speed of the idle breathing pulse.")]
    [SerializeField] private float breatheSpeed = 1.2f;

    [Tooltip("Magnitude of the idle breathing scale oscillation.")]
    [SerializeField] private float breatheAmount = 0.06f;

    [Header("Colors")]
    [Tooltip("Emission color when taking damage — brief flash.")]
    [SerializeField] private Color damageFlashColor = Color.white;

    [Tooltip("Duration of the damage flash in seconds.")]
    [SerializeField] private float damageFlashDuration = 0.15f;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    private Renderer _renderer;
    private Material _material;
    private NetworkPlayer _networkPlayer;

    /// Whether the player is currently moving — drives trail visibility.
    private bool _isMoving;

    /// Whether a dash pulse is currently animating.
    private bool _dashPulsing;

    /// Timer tracking current dash pulse progress.
    private float _dashPulseTimer;

    /// Whether a damage flash is currently active.
    private bool _flashing;

    /// Timer tracking current damage flash progress.
    private float _flashTimer;

    /// Whether this player is dead — stops breathing pulse.
    private bool _isDead;

    /// Cached base emission color (player's assigned color).
    private Color _baseEmission;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();

        // Create a material instance so each player has independent color.
        _material = new Material(_renderer.material);
        _renderer.material = _material;

        // Auto-resolve trail if not assigned.
        if (trailRenderer == null)
            trailRenderer = GetComponentInChildren<TrailRenderer>();
    }

    public override void OnNetworkSpawn()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();
        if (_networkPlayer == null) return;

        // Apply initial color and subscribe to color changes.
        ApplyPlayerColor(_networkPlayer.playerColor.Value);
        _networkPlayer.playerColor.OnValueChanged += OnColorChanged;
        _networkPlayer.health.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (_networkPlayer == null) return;
        _networkPlayer.playerColor.OnValueChanged -= OnColorChanged;
        _networkPlayer.health.OnValueChanged -= OnHealthChanged;
    }

    private void Update()
    {
        if (_isDead) return;
        UpdateBreathe();
        UpdateDashPulse();
        UpdateDamageFlash();
        UpdateTrail();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC API — called by PlayerController
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerController each frame with current movement state.
    /// Enables trail when moving, fades it out when stationary.
    /// </summary>
    public void SetMoving(bool isMoving)
    {
        _isMoving = isMoving;
    }

    /// <summary>
    /// Called by PlayerController on double-tap dash.
    /// Triggers a brief scale pulse to visually confirm the burst.
    /// </summary>
    public void TriggerDash()
    {
        _dashPulsing = true;
        _dashPulseTimer = 0f;
    }

    /// <summary>
    /// Called by PlayerController on death.
    /// Fades the player out and disables further visual updates.
    /// </summary>
    public void TriggerDeath()
    {
        _isDead = true;
        StartCoroutine(DeathFade());
    }

    /// <summary>
    /// Called by NetworkPlayer health change callback.
    /// Triggers a brief white emission flash to confirm damage received.
    /// </summary>
    public void TriggerDamageFlash()
    {
        _flashing = true;
        _flashTimer = 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COLOR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the player's assigned color to the material and trail.
    /// Called on spawn and whenever the NetworkVariable changes.
    /// Uses HDR emission for the glow effect — brighter than albedo.
    /// </summary>
    private void ApplyPlayerColor(Color color)
    {
        _baseEmission = color * 1.5f; // HDR boost for glow
        _material.color = color;

        if (_material.HasProperty("_EmissionColor"))
        {
            _material.EnableKeyword("_EMISSION");
            _material.SetColor("_EmissionColor", _baseEmission);
        }

        if (trailRenderer != null)
        {
            trailRenderer.startColor = new Color(color.r, color.g, color.b, 0.8f);
            trailRenderer.endColor = new Color(color.r, color.g, color.b, 0f);
        }
    }

    private void OnColorChanged(Color previous, Color current)
        => ApplyPlayerColor(current);

    // ─────────────────────────────────────────────────────────────────────────
    // ANIMATIONS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gentle scale oscillation — gives the player a "breathing" organic feel.
    /// Matches the deep space / bioluminescent aesthetic.
    /// Disabled during dash pulse and death.
    /// </summary>
    private void UpdateBreathe()
    {
        if (_dashPulsing) return;
        float breathe = 1f + Mathf.Sin(Time.time * breatheSpeed) * breatheAmount;
        transform.localScale = Vector3.one * baseScale * breathe;
    }

    /// <summary>
    /// Quick scale pulse on dash — expands then snaps back.
    /// Runs for dashPulseDuration seconds using EaseOutCubic.
    /// </summary>
    private void UpdateDashPulse()
    {
        if (!_dashPulsing) return;

        _dashPulseTimer += Time.deltaTime;
        float t = _dashPulseTimer / dashPulseDuration;

        if (t >= 1f)
        {
            _dashPulsing = false;
            transform.localScale = Vector3.one * baseScale;
            return;
        }

        // Expand then contract — peaks at t=0.3
        float scale = t < 0.3f
            ? Mathf.Lerp(baseScale, baseScale * dashScalePulse, t / 0.3f)
            : Mathf.Lerp(baseScale * dashScalePulse, baseScale, (t - 0.3f) / 0.7f);

        transform.localScale = Vector3.one * scale;
    }

    /// <summary>
    /// Brief white emission flash on damage.
    /// Interpolates emission from white back to base color.
    /// </summary>
    private void UpdateDamageFlash()
    {
        if (!_flashing) return;

        _flashTimer += Time.deltaTime;
        float t = _flashTimer / damageFlashDuration;

        if (t >= 1f)
        {
            _flashing = false;
            if (_material.HasProperty("_EmissionColor"))
                _material.SetColor("_EmissionColor", _baseEmission);
            return;
        }

        Color flash = Color.Lerp(damageFlashColor * 2f, _baseEmission, t);
        if (_material.HasProperty("_EmissionColor"))
            _material.SetColor("_EmissionColor", flash);
    }

    /// <summary>
    /// Enables trail when moving, disables when stationary.
    /// Trail color is already set to player color in ApplyPlayerColor.
    /// </summary>
    private void UpdateTrail()
    {
        if (trailRenderer == null) return;
        trailRenderer.emitting = _isMoving;
    }

    /// <summary>
    /// Death fade — scales down and reduces alpha over 0.5 seconds.
    /// </summary>
    private System.Collections.IEnumerator DeathFade()
    {
        float elapsed = 0f;
        float duration = 0.5f;
        Vector3 startScale = transform.localScale;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);

            Color c = _material.color;
            c.a = Mathf.Lerp(1f, 0f, t);
            _material.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HEALTH CALLBACK
    // ─────────────────────────────────────────────────────────────────────────

    private void OnHealthChanged(float previous, float current)
    {
        if (current < previous)
            TriggerDamageFlash();
    }
}