using UnityEngine;

/// <summary>
/// FeedbackService — centralized audio and haptic feedback for all input events.
///
/// Single point of truth for all non-visual feedback in Drift. UIFeedback calls
/// FeedbackService.Play(FeedbackType) alongside every visual effect — this class
/// handles audio and haptics without UIFeedback needing to know about AudioClips
/// or platform-specific haptic APIs.
///
/// Current state: STUBBED. All Play() calls are safe no-ops.
/// Audio and haptics are wired in feature/ui-feedback-audio when assets are ready.
///
/// To add a sound later:
///   1. Assign an AudioClip to the matching slot in the Inspector
///   2. That's it — the Play() routing is already wired
///
/// To add haptics later:
///   1. Uncomment the platform block in TriggerHaptic()
///   2. Assign haptic intensity values per FeedbackType in the Inspector
///
/// Attach to: the same persistent GameObject as InputManager and UIFeedback.
/// There should be exactly one FeedbackService in the scene.
/// </summary>
public class FeedbackService : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // FEEDBACK TYPE ENUM
    // Mirrors every input event that has a visual in UIFeedback.
    // ─────────────────────────────────────────────────────────────────────────

    public enum FeedbackType
    {
        TapPop,             // Single tap — precision collect
        DashFlare,          // Double tap — dash burst
        HoldTick,           // Hold progress — periodic tick while charging
        HoldComplete,       // Hold charge reached full — area collect fires
        ThreeFingerBurst,   // Three-finger burst collect
        FourFingerSearch,   // Four-finger searching pulse (beam placeholder)
        DomainExpand,       // Five-finger domain expand
        TiltStart,          // Tilt crosses threshold from neutral — drift begins
        TiltAimed,          // Tilt active during hold — aimed collect cone
        ZoomIn,             // Two-finger spread
        ZoomOut,            // Two-finger pinch
        Swipe,              // Single-finger movement trail (stub — PlayerVisuals owns trail)
        Calibrated,         // Tilt neutral baseline set
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static FeedbackService Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — Audio Clips (all optional — no-op when unassigned)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Audio Clips (assign when audio assets are ready — all optional)")]
    [Tooltip("TODO: short click/pop for single tap precision collect.")]
    [SerializeField] private AudioClip tapPopClip;

    [Tooltip("TODO: whoosh for double-tap dash burst.")]
    [SerializeField] private AudioClip dashFlareClip;

    [Tooltip("TODO: soft tick played periodically during hold charge.")]
    [SerializeField] private AudioClip holdTickClip;

    [Tooltip("TODO: satisfying release sound when hold completes.")]
    [SerializeField] private AudioClip holdCompleteClip;

    [Tooltip("TODO: expanding burst sound for three-finger collect.")]
    [SerializeField] private AudioClip threeFingerBurstClip;

    [Tooltip("TODO: searching/scanning loop for four-finger share.")]
    [SerializeField] private AudioClip fourFingerSearchClip;

    [Tooltip("TODO: deep pulse/boom for domain expand.")]
    [SerializeField] private AudioClip domainExpandClip;

    [Tooltip("TODO: subtle start sound when tilt drift activates.")]
    [SerializeField] private AudioClip tiltStartClip;

    [Tooltip("TODO: confirmation chime when tilt neutral is calibrated.")]
    [SerializeField] private AudioClip calibratedClip;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — Haptics
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Haptics")]
    [Tooltip("Master switch — disable to turn off all haptic feedback.")]
    [SerializeField] private bool hapticsEnabled = true;

    [Tooltip("Light haptic intensity (0-1). Used for tap, tick, tilt.")]
    [SerializeField][Range(0f, 1f)] private float lightHapticIntensity = 0.3f;

    [Tooltip("Medium haptic intensity (0-1). Used for dash, hold complete, zoom.")]
    [SerializeField][Range(0f, 1f)] private float mediumHapticIntensity = 0.55f;

    [Tooltip("Heavy haptic intensity (0-1). Used for burst, domain expand.")]
    [SerializeField][Range(0f, 1f)] private float heavyHapticIntensity = 0.85f;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — Hold Tick Rate
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Hold Tick")]
    [Tooltip("How often (seconds) the hold tick sound/haptic fires while charging.")]
    [SerializeField] private float holdTickInterval = 0.3f;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// AudioSource used for all one-shot feedback sounds.
    private AudioSource _audioSource;

    /// Tracks time since last hold tick so we don't fire every frame.
    private float _holdTickTimer = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Add an AudioSource on this GameObject if none exists.
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D — UI feedback is always flat.
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Play audio and haptic feedback for the given event type.
    /// Safe no-op when audio clips are unassigned or haptics are disabled.
    /// Called by UIFeedback alongside every visual effect.
    /// </summary>
    public void Play(FeedbackType type)
    {
        switch (type)
        {
            case FeedbackType.TapPop:
                PlayClip(tapPopClip);
                TriggerHaptic(HapticLevel.Light);
                break;

            case FeedbackType.DashFlare:
                PlayClip(dashFlareClip);
                TriggerHaptic(HapticLevel.Medium);
                break;

            case FeedbackType.HoldTick:
                // Rate-limit the tick so it fires on interval, not every frame.
                _holdTickTimer += Time.deltaTime;
                if (_holdTickTimer >= holdTickInterval)
                {
                    _holdTickTimer = 0f;
                    PlayClip(holdTickClip);
                    TriggerHaptic(HapticLevel.Light);
                }
                break;

            case FeedbackType.HoldComplete:
                _holdTickTimer = 0f;
                PlayClip(holdCompleteClip);
                TriggerHaptic(HapticLevel.Medium);
                break;

            case FeedbackType.ThreeFingerBurst:
                PlayClip(threeFingerBurstClip);
                TriggerHaptic(HapticLevel.Heavy);
                break;

            case FeedbackType.FourFingerSearch:
                // No audio spam — four-finger fires every frame while held.
                // Haptic only on first frame; UIFeedback manages re-trigger guard.
                TriggerHaptic(HapticLevel.Light);
                break;

            case FeedbackType.DomainExpand:
                PlayClip(domainExpandClip);
                TriggerHaptic(HapticLevel.Heavy);
                break;

            case FeedbackType.TiltStart:
                PlayClip(tiltStartClip);
                TriggerHaptic(HapticLevel.Light);
                break;

            case FeedbackType.TiltAimed:
                // No audio — fires every frame during aim. Visual feedback is sufficient.
                break;

            case FeedbackType.ZoomIn:
            case FeedbackType.ZoomOut:
                // No audio per pinch delta — fires every frame. Haptic only.
                TriggerHaptic(HapticLevel.Light);
                break;

            case FeedbackType.Calibrated:
                PlayClip(calibratedClip);
                TriggerHaptic(HapticLevel.Medium);
                break;

            case FeedbackType.Swipe:
                // No feedback for swipe — movement trail is PlayerVisuals' domain.
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIO
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays a one-shot audio clip. Safe no-op when clip is null.
    /// </summary>
    private void PlayClip(AudioClip clip)
    {
        if (clip == null || _audioSource == null) return;
        _audioSource.PlayOneShot(clip);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAPTICS
    // ─────────────────────────────────────────────────────────────────────────

    private enum HapticLevel { Light, Medium, Heavy }

    /// <summary>
    /// Triggers a haptic impulse at the given intensity level.
    /// Currently stubbed — uncomment the platform block below when ready to wire.
    ///
    /// Unity does not have a built-in cross-platform haptics API for mobile.
    /// Options when implementing:
    ///   Android: AndroidJavaClass "android.os.VibrationEffect" via JNI
    ///   iOS:     UIImpactFeedbackGenerator via a native plugin or wrapper
    ///   Recommended: import a lightweight haptics package (e.g. Lofelt Nice Vibrations)
    ///                and replace the stub calls below with its API.
    /// </summary>
    private void TriggerHaptic(HapticLevel level)
    {
        if (!hapticsEnabled) return;

        float intensity = level switch
        {
            HapticLevel.Light => lightHapticIntensity,
            HapticLevel.Medium => mediumHapticIntensity,
            HapticLevel.Heavy => heavyHapticIntensity,
            _ => mediumHapticIntensity
        };

        // ── TODO: replace stub with real haptic call ──────────────────────────
        // Example using Nice Vibrations (Lofelt):
        //   HapticPatterns.PlayConstant(intensity, 0f, 0.05f);
        //
        // Example using raw Android vibration (no package needed):
        //   #if UNITY_ANDROID && !UNITY_EDITOR
        //   using (var vibrator = new AndroidJavaClass("android.os.Vibrator"))
        //       vibrator.Call("vibrate", (long)(intensity * 50));
        //   #endif
        //
        // For now: safe no-op. Debug log only in editor to confirm calls are routing.
        // ─────────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        // Uncomment to verify haptic routing during development:
        // Debug.Log($"[Haptic] {level} ({intensity:F2})");
#endif
    }
}