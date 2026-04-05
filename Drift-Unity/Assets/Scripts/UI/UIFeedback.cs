using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UIFeedback — cosmetic visual feedback layer for all player-facing input responses.
///
/// Subscribes to GestureEvents and translates each event into a visible effect.
/// This class owns NO game logic — it only reacts to events and drives visuals.
/// All gameplay consequence (movement, collection, networking) lives elsewhere.
///
/// Effects implemented:
///   OnSingleTap       → screen-space pop flash at tap position
///   OnDoubleTap       → dash flare around player anchor
///   OnHoldProgress    → radial fill circle expanding 0→1
///   OnHoldComplete    → burst flash at full charge
///   OnThreeFingerHold → 3 concentric expanding rings
///   OnFourFingerHold  → pulsing "searching" ring (beam wired when NetworkPlayer exists)
///   OnFiveFingerSplay → full-screen domain expand pulse
///   OnTiltChanged     → directional arrow at player edge
///   OnTiltAimed       → asymmetric cone offset on the hold-pulse circle
///   OnPinchDelta      → zoom level indicator fade-in/out
///   OnSwipe           → movement trail stub (PlayerVisuals owns this when built)
///
/// Player anchor:
///   Finds GameObject tagged "Player" at Start. Falls back to screen center if
///   no player exists yet. Auto-reconnects each frame so it works before
///   PlayerController is built and after it is instantiated at runtime.
///
/// Particle system hooks:
///   All particle effects are marked with // TODO: assign in Inspector.
///   Logic paths call Play() on the ParticleSystem reference if assigned;
///   they are safe no-ops when unassigned. Assign real VFX prefabs later
///   without any code changes.
///
/// Attach to: the same persistent GameObject as InputManager (e.g. GameManager).
/// Requires: a Canvas with a RectTransform in the scene, assigned via Inspector.
/// </summary>
public class UIFeedback : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR REFERENCES — Canvas / UI
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Canvas References")]
    [Tooltip("The screen-space overlay Canvas. All UI feedback elements live here.")]
    [SerializeField] private Canvas feedbackCanvas;

    [Tooltip("Radial fill Image (Image Type: Filled, Fill Method: Radial 360). " +
             "Driven by OnHoldProgress. Assign the Image component, not the GameObject.")]
    [SerializeField] private Image holdRadialFill;

    [Tooltip("Image used for the tap pop flash. Assign a soft circle sprite.")]
    [SerializeField] private Image tapPopImage;

    [Tooltip("Image used for the dash flare around the player. Assign a glow/ring sprite.")]
    [SerializeField] private Image dashFlareImage;

    [Tooltip("Image used for the tilt direction arrow at the player edge.")]
    [SerializeField] private Image tiltArrowImage;

    [Tooltip("Image used for the aimed tilt cone overlay on the hold circle.")]
    [SerializeField] private Image tiltConeImage;

    [Tooltip("Image used for the zoom level indicator (pinch feedback).")]
    [SerializeField] private Image zoomIndicatorImage;

    [Tooltip("Text label showing zoom level (optional — can be null).")]
    [SerializeField] private Text zoomLevelText;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR REFERENCES — Particle Systems (all optional — safe when null)
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Particle Systems (assign in Inspector — all optional)")]
    [Tooltip("TODO: assign a burst particle VFX for hold-complete flash.")]
    [SerializeField] private ParticleSystem holdBurstParticles;

    [Tooltip("TODO: assign a ring-expand VFX for three-finger burst collect.")]
    [SerializeField] private ParticleSystem threeFingerRingParticles;

    [Tooltip("TODO: assign a radial pulse VFX for five-finger domain expand.")]
    [SerializeField] private ParticleSystem domainPulseParticles;

    [Tooltip("TODO: assign a speed trail VFX for double-tap dash flare.")]
    [SerializeField] private ParticleSystem dashTrailParticles;

    // ─────────────────────────────────────────────────────────────────────────
    // TUNING
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Tap Pop")]
    [Tooltip("Duration (seconds) of the tap pop flash animation.")]
    [SerializeField] private float tapPopDuration = 0.25f;

    [Tooltip("Maximum scale the tap pop reaches at peak.")]
    [SerializeField] private float tapPopMaxScale = 1.8f;

    [Header("Dash Flare")]
    [Tooltip("Duration (seconds) of the dash flare animation.")]
    [SerializeField] private float dashFlareDuration = 0.35f;

    [Tooltip("Maximum scale the dash flare ring reaches.")]
    [SerializeField] private float dashFlareMaxScale = 2.5f;

    [Header("Hold Pulse")]
    [Tooltip("Color of the radial fill when charging.")]
    [SerializeField] private Color holdFillColor = new Color(0.3f, 0.8f, 1f, 0.7f);

    [Tooltip("Color flash of the radial fill on hold complete.")]
    [SerializeField] private Color holdCompleteColor = new Color(1f, 1f, 0.4f, 1f);

    [Header("Three-Finger Rings")]
    [Tooltip("How many concentric rings to show on three-finger burst.")]
    [SerializeField] private int threeFingerRingCount = 3;

    [Tooltip("Duration (seconds) for each ring to expand and fade.")]
    [SerializeField] private float threeFingerRingDuration = 0.6f;

    [Tooltip("Stagger delay (seconds) between each ring launch.")]
    [SerializeField] private float threeFingerRingStagger = 0.12f;

    [Header("Four-Finger Share")]
    [Tooltip("Pulse rate (seconds per cycle) of the searching ring.")]
    [SerializeField] private float fourFingerPulseRate = 0.5f;

    [Header("Five-Finger Domain Pulse")]
    [Tooltip("Duration (seconds) of the full-screen domain expand animation.")]
    [SerializeField] private float domainPulseDuration = 0.7f;

    [Header("Tilt Arrow")]
    [Tooltip("Distance (screen pixels) from player center to tilt arrow.")]
    [SerializeField] private float tiltArrowOffset = 80f;

    [Tooltip("Minimum tilt magnitude (0-1) before arrow becomes visible.")]
    [SerializeField] private float tiltArrowThreshold = 0.05f;

    [Header("Zoom Indicator")]
    [Tooltip("Duration (seconds) the zoom indicator stays visible after pinch ends.")]
    [SerializeField] private float zoomFadeDelay = 1.2f;

    [Tooltip("Duration (seconds) for the zoom indicator to fade out.")]
    [SerializeField] private float zoomFadeDuration = 0.4f;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Cached player transform — resolved each frame via tag lookup with screen-center fallback.
    private Transform _playerTransform;

    /// Current screen-space position of the player anchor (or screen center).
    private Vector2 _playerScreenPos;

    /// Whether a four-finger share gesture is currently active.
    private bool _fourFingerActive = false;

    /// Coroutine handle for the zoom indicator fade — cancelled and restarted on each pinch.
    private Coroutine _zoomFadeCoroutine;

    /// Coroutine handle for the dash flare — prevents overlapping animations.
    private Coroutine _dashFlareCoroutine;

    /// Whether a three-finger ring burst coroutine is currently running.
    private bool _threeFingerBurstActive = false;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        InitializeUIElements();
    }

    private void OnEnable()
    {
        SubscribeToEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromEvents();
    }

    private void Update()
    {
        RefreshPlayerAnchor();
        UpdateTiltArrow();
        UpdateFourFingerFeedback();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INITIALIZATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Hides all feedback elements at startup so nothing is visible before input.
    /// Called in Awake so state is clean before any events can fire.
    /// </summary>
    private void InitializeUIElements()
    {
        SetImageAlpha(tapPopImage, 0f);
        SetImageAlpha(dashFlareImage, 0f);
        SetImageAlpha(tiltArrowImage, 0f);
        SetImageAlpha(tiltConeImage, 0f);
        SetImageAlpha(zoomIndicatorImage, 0f);

        if (holdRadialFill != null)
        {
            holdRadialFill.fillAmount = 0f;
            holdRadialFill.color = holdFillColor;
            SetImageAlpha(holdRadialFill, 0f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYER ANCHOR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the player screen position each frame.
    /// Looks for a GameObject tagged "Player". Falls back to screen center.
    /// This makes UIFeedback functional before PlayerController is built,
    /// and auto-connects once a player exists — no manual wiring needed.
    /// </summary>
    private void RefreshPlayerAnchor()
    {
        if (_playerTransform == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
                _playerTransform = player.transform;
        }

        if (_playerTransform != null && Camera.main != null)
        {
            _playerScreenPos = Camera.main.WorldToScreenPoint(_playerTransform.position);
        }
        else
        {
            // Fallback: screen center.
            _playerScreenPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EVENT SUBSCRIPTIONS
    // ─────────────────────────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        GestureEvents.OnSingleTap += HandleSingleTap;
        GestureEvents.OnDoubleTap += HandleDoubleTap;
        GestureEvents.OnHoldProgress += HandleHoldProgress;
        GestureEvents.OnHoldComplete += HandleHoldComplete;
        GestureEvents.OnThreeFingerHold += HandleThreeFingerHold;
        GestureEvents.OnFourFingerHold += HandleFourFingerHold;
        GestureEvents.OnFiveFingerSplay += HandleFiveFingerSplay;
        GestureEvents.OnTiltChanged += HandleTiltChanged;
        GestureEvents.OnTiltAimed += HandleTiltAimed;
        GestureEvents.OnPinchDelta += HandlePinchDelta;
        GestureEvents.OnSwipe += HandleSwipe;
    }

    private void UnsubscribeFromEvents()
    {
        GestureEvents.OnSingleTap -= HandleSingleTap;
        GestureEvents.OnDoubleTap -= HandleDoubleTap;
        GestureEvents.OnHoldProgress -= HandleHoldProgress;
        GestureEvents.OnHoldComplete -= HandleHoldComplete;
        GestureEvents.OnThreeFingerHold -= HandleThreeFingerHold;
        GestureEvents.OnFourFingerHold -= HandleFourFingerHold;
        GestureEvents.OnFiveFingerSplay -= HandleFiveFingerSplay;
        GestureEvents.OnTiltChanged -= HandleTiltChanged;
        GestureEvents.OnTiltAimed -= HandleTiltAimed;
        GestureEvents.OnPinchDelta -= HandlePinchDelta;
        GestureEvents.OnSwipe -= HandleSwipe;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SINGLE TAP — screen-space pop flash at touch position
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a confirmed single tap by starting the pop animation at the
    /// tap screen position and triggering the corresponding audio/haptic feedback.
    /// The visual pop and non-visual feedback are always fired together so they
    /// are never accidentally decoupled by future edits.
    /// </summary>
    private void HandleSingleTap(Vector2 screenPos)
    {
        if (tapPopImage == null) return;
        StartCoroutine(AnimateTapPop(screenPos));
        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.TapPop);
    }

    private IEnumerator AnimateTapPop(Vector2 screenPos)
    {
        // Reposition to tap point in screen space.
        tapPopImage.rectTransform.position = screenPos;
        tapPopImage.transform.localScale = Vector3.one * 0.3f;
        SetImageAlpha(tapPopImage, 1f);

        float elapsed = 0f;
        while (elapsed < tapPopDuration)
        {
            float t = elapsed / tapPopDuration;

            // Scale up fast, hold briefly, then back down — feels snappy.
            float scale = Mathf.Lerp(0.3f, tapPopMaxScale, EaseOutCubic(t));
            tapPopImage.transform.localScale = Vector3.one * scale;

            // Fade out in the second half.
            float alpha = t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) * 2f);
            SetImageAlpha(tapPopImage, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        SetImageAlpha(tapPopImage, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DOUBLE TAP — dash flare ring around player
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a double tap by playing the dash flare ring animation and
    /// optional trail particles anchored to the player position.
    /// Cancels any in-progress flare coroutine so rapid double-taps each
    /// produce a clean, non-overlapping animation.
    /// Triggers medium audio/haptic feedback to convey the burst speed feel.
    /// </summary>
    private void HandleDoubleTap()
    {
        if (dashFlareImage == null) return;

        if (_dashFlareCoroutine != null)
            StopCoroutine(_dashFlareCoroutine);

        _dashFlareCoroutine = StartCoroutine(AnimateDashFlare());

        if (dashTrailParticles != null)
            dashTrailParticles.Play();

        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.DashFlare);
    }

    private IEnumerator AnimateDashFlare()
    {
        dashFlareImage.rectTransform.position = _playerScreenPos;
        dashFlareImage.transform.localScale = Vector3.one * 0.5f;
        SetImageAlpha(dashFlareImage, 1f);

        float elapsed = 0f;
        while (elapsed < dashFlareDuration)
        {
            float t = elapsed / dashFlareDuration;
            float scale = Mathf.Lerp(0.5f, dashFlareMaxScale, EaseOutQuad(t));
            float alpha = Mathf.Lerp(1f, 0f, EaseInCubic(t));

            dashFlareImage.transform.localScale = Vector3.one * scale;
            SetImageAlpha(dashFlareImage, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        SetImageAlpha(dashFlareImage, 0f);
        _dashFlareCoroutine = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HOLD PROGRESS — radial fill circle
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives the radial fill image to match hold charge progress (0–1).
    /// Repositions to the player anchor each call so the fill circle follows
    /// the player if they are moving while holding.
    /// Alpha increases with progress to create a building intensity feel.
    /// FeedbackService rate-limits the hold tick internally so calling Play
    /// here every frame does not produce audio/haptic spam.
    /// </summary>
    private void HandleHoldProgress(float progress)
    {
        if (holdRadialFill == null) return;

        holdRadialFill.rectTransform.position = _playerScreenPos;
        holdRadialFill.fillAmount = progress;
        holdRadialFill.color = holdFillColor;
        SetImageAlpha(holdRadialFill, Mathf.Lerp(0.4f, 0.9f, progress));

        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.HoldTick);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HOLD COMPLETE — burst flash
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a fully charged hold-pulse completing.
    /// Flashes the radial fill to the complete color and fades it out.
    /// Fires optional burst particles for a satisfying release effect.
    /// Triggers medium audio/haptic feedback — this is the payoff moment
    /// of the hold gesture and should feel distinct from the charge ticks.
    /// </summary>
    private void HandleHoldComplete()
    {
        if (holdRadialFill != null)
            StartCoroutine(AnimateHoldComplete());

        if (holdBurstParticles != null)
            holdBurstParticles.Play();

        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.HoldComplete);
    }

    private IEnumerator AnimateHoldComplete()
    {
        holdRadialFill.fillAmount = 1f;
        holdRadialFill.color = holdCompleteColor;
        SetImageAlpha(holdRadialFill, 1f);

        // Hold the flash briefly.
        yield return new WaitForSeconds(0.1f);

        // Fade out.
        float elapsed = 0f;
        float fadeDuration = 0.25f;
        while (elapsed < fadeDuration)
        {
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);
            SetImageAlpha(holdRadialFill, alpha);
            elapsed += Time.deltaTime;
            yield return null;
        }

        holdRadialFill.fillAmount = 0f;
        SetImageAlpha(holdRadialFill, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // THREE-FINGER HOLD — 3 concentric expanding rings
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a three-finger hold by launching the staggered concentric
    /// ring burst animation from the player anchor.
    /// Guarded by _threeFingerBurstActive so the animation is not re-triggered
    /// on every frame while the gesture is held — each burst runs to completion
    /// before a new one can start.
    /// Triggers heavy audio/haptic feedback to match the large-area collect feel.
    /// </summary>
    private void HandleThreeFingerHold()
    {
        if (_threeFingerBurstActive) return;
        StartCoroutine(AnimateThreeFingerRings());

        if (threeFingerRingParticles != null)
            threeFingerRingParticles.Play();

        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.ThreeFingerBurst);
    }

    private IEnumerator AnimateThreeFingerRings()
    {
        _threeFingerBurstActive = true;

        for (int i = 0; i < threeFingerRingCount; i++)
        {
            StartCoroutine(AnimateSingleRing(i));
            yield return new WaitForSeconds(threeFingerRingStagger);
        }

        // Wait for the last ring to finish before allowing re-trigger.
        yield return new WaitForSeconds(threeFingerRingDuration);
        _threeFingerBurstActive = false;
    }

    /// <summary>
    /// Animates one ring — expands from player position and fades out.
    /// Uses a temporary Image clone so rings are independent.
    /// </summary>
    private IEnumerator AnimateSingleRing(int ringIndex)
    {
        if (dashFlareImage == null) yield break;

        // Clone the flare image as a ring proxy.
        Image ring = Instantiate(dashFlareImage, feedbackCanvas.transform);
        ring.rectTransform.position = _playerScreenPos;
        ring.transform.localScale = Vector3.one * (0.4f + ringIndex * 0.2f);
        SetImageAlpha(ring, 0.8f);

        float elapsed = 0f;
        float maxScale = 1.5f + ringIndex * 0.8f;

        while (elapsed < threeFingerRingDuration)
        {
            float t = elapsed / threeFingerRingDuration;
            float scale = Mathf.Lerp(ring.transform.localScale.x, maxScale, EaseOutQuad(t));
            float alpha = Mathf.Lerp(0.8f, 0f, EaseInQuad(t));

            ring.transform.localScale = Vector3.one * scale;
            SetImageAlpha(ring, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(ring.gameObject);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOUR-FINGER HOLD — pulsing "searching for ally" ring
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a four-finger hold by setting the active flag consumed
    /// by UpdateFourFingerFeedback() in Update().
    /// The pulsing ring animation is driven per-frame in Update rather than
    /// via a coroutine so it naturally stops the moment the gesture ends
    /// without needing explicit cancellation.
    ///
    /// NOTE: Light haptic fires here on every frame the gesture is active.
    /// FeedbackService does not rate-limit FourFingerSearch — if haptic spam
    /// becomes an issue, add a rate-limit guard here similar to HoldTick.
    /// The beam direction toward a networked partner will be wired in
    /// feature/netcode-foundation once NetworkPlayer positions are available.
    /// See ADR-007.
    /// </summary>
    private void HandleFourFingerHold()
    {
        _fourFingerActive = true;
        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.FourFingerSearch);
    }

    /// <summary>
    /// Drives the four-finger pulse animation each frame.
    /// Detects when the event stops firing (no call this frame) and clears.
    /// </summary>
    private float _fourFingerPulseTimer = 0f;
    private bool _fourFingerWasActive = false;

    private void UpdateFourFingerFeedback()
    {
        if (!_fourFingerActive)
        {
            if (_fourFingerWasActive)
            {
                // Event just stopped — hide the ring.
                SetImageAlpha(dashFlareImage, 0f);
                _fourFingerWasActive = false;
                _fourFingerPulseTimer = 0f;
            }
            return;
        }

        _fourFingerWasActive = true;
        _fourFingerPulseTimer += Time.deltaTime;

        // Sinusoidal pulse — scale and alpha oscillate.
        float pulse = Mathf.Sin(_fourFingerPulseTimer * Mathf.PI / fourFingerPulseRate) * 0.5f + 0.5f;

        if (dashFlareImage != null)
        {
            dashFlareImage.rectTransform.position = _playerScreenPos;
            dashFlareImage.transform.localScale = Vector3.one * Mathf.Lerp(1.0f, 1.6f, pulse);
            SetImageAlpha(dashFlareImage, Mathf.Lerp(0.2f, 0.7f, pulse));
        }

        // Reset the flag — it will be set again next frame if the gesture continues.
        _fourFingerActive = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIVE-FINGER SPLAY — full-screen domain expand pulse
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a five-finger splay by launching the full-screen domain
    /// expand pulse animation from screen center.
    /// This is the highest-stakes cooperative ability — the feedback is
    /// intentionally dramatic (large scale, full-screen) to match.
    /// Triggers heavy audio/haptic feedback as the strongest response
    /// in the escalating multi-finger ability hierarchy.
    /// </summary>
    private void HandleFiveFingerSplay()
    {
        StartCoroutine(AnimateDomainPulse());

        if (domainPulseParticles != null)
            domainPulseParticles.Play();

        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.DomainExpand);
    }

    private IEnumerator AnimateDomainPulse()
    {
        if (holdRadialFill == null) yield break;

        holdRadialFill.rectTransform.position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        holdRadialFill.fillAmount = 1f;
        holdRadialFill.color = new Color(0.4f, 1f, 0.6f, 1f);
        holdRadialFill.transform.localScale = Vector3.one * 0.5f;
        SetImageAlpha(holdRadialFill, 0.9f);

        float elapsed = 0f;
        while (elapsed < domainPulseDuration)
        {
            float t = elapsed / domainPulseDuration;
            float scale = Mathf.Lerp(0.5f, 6f, EaseOutCubic(t));
            float alpha = Mathf.Lerp(0.9f, 0f, EaseInQuad(t));

            holdRadialFill.transform.localScale = Vector3.one * scale;
            SetImageAlpha(holdRadialFill, alpha);

            elapsed += Time.deltaTime;
            yield return null;
        }

        holdRadialFill.transform.localScale = Vector3.one;
        holdRadialFill.fillAmount = 0f;
        SetImageAlpha(holdRadialFill, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TILT CHANGED — directional arrow at player edge
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Caches the latest tilt vector. UpdateTiltArrow() reads this each frame
    /// and positions/rotates the tilt arrow image at the player edge.
    /// </summary>
    private Vector2 _latestTilt = Vector2.zero;

    /// Whether tilt was active last frame — used to detect the inactive→active
    /// transition so TiltStart feedback fires once on crossing the threshold,
    /// not every frame while tilt is sustained.
    private bool _tiltWasActive = false;

    /// <summary>
    /// Caches the latest tilt vector each frame for use by UpdateTiltArrow().
    /// Kept separate from UpdateTiltArrow so the event fires once per frame
    /// while the per-frame arrow positioning runs in Update independently.
    /// </summary>
    private void HandleTiltChanged(Vector2 tilt)
    {
        _latestTilt = tilt;
    }

    /// <summary>
    /// Positions and rotates the tilt direction arrow at the player edge each frame.
    /// The arrow is hidden when tilt magnitude is below tiltArrowThreshold,
    /// fades in and strengthens as lean increases, and rotates to face the lean direction.
    ///
    /// Also detects the inactive→active tilt threshold crossing and fires
    /// TiltStart feedback exactly once per crossing — not on every sustained frame.
    /// This gives a clean "drift begins" audio/haptic cue without spam.
    ///
    /// Screen Y inversion: accelerometer Y increases upward, screen Y increases
    /// downward — negating _latestTilt.y corrects the arrow direction to match
    /// the visual lean direction as seen on screen.
    /// </summary>
    private void UpdateTiltArrow()
    {
        if (tiltArrowImage == null) return;

        float magnitude = _latestTilt.magnitude;
        bool tiltActive = magnitude >= tiltArrowThreshold;

        // Fire TiltStart feedback once when crossing from inactive to active.
        if (tiltActive && !_tiltWasActive)
            FeedbackService.Instance?.Play(FeedbackService.FeedbackType.TiltStart);

        _tiltWasActive = tiltActive;

        if (!tiltActive)
        {
            SetImageAlpha(tiltArrowImage, 0f);
            return;
        }

        // Invert Y: accelerometer Y up = screen Y down.
        Vector2 screenTilt = new Vector2(_latestTilt.x, -_latestTilt.y);
        Vector2 arrowScreenPos = _playerScreenPos + screenTilt.normalized * tiltArrowOffset;

        tiltArrowImage.rectTransform.position = arrowScreenPos;

        // Rotate arrow to face the lean direction in screen space.
        float angle = Mathf.Atan2(screenTilt.y, screenTilt.x) * Mathf.Rad2Deg;
        tiltArrowImage.rectTransform.rotation = Quaternion.Euler(0f, 0f, angle);

        // Alpha scales with lean strength — subtle at threshold, solid at full lean.
        SetImageAlpha(tiltArrowImage, Mathf.Clamp01(magnitude * 2f));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TILT AIMED — asymmetric cone on hold-pulse circle
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to simultaneous tilt + hold-pulse by showing the aimed collect cone.
    /// The cone is rotated to face the lean direction and scaled by tilt magnitude —
    /// a stronger lean produces a wider, more opaque cone indicating a larger
    /// offset in the collection radius.
    ///
    /// Screen Y is inverted relative to accelerometer Y (screen Y increases downward,
    /// accelerometer Y increases upward) — the negation on screenDir.y corrects this.
    ///
    /// No audio is triggered here — TiltAimed fires every frame during aiming and
    /// audio would be continuous noise. Visual feedback is sufficient for aiming.
    /// </summary>
    private void HandleTiltAimed(Vector2 direction, float magnitude)
    {
        if (tiltConeImage == null) return;

        tiltConeImage.rectTransform.position = _playerScreenPos;

        // Invert Y: screen space has Y increasing downward, tilt Y increases upward.
        Vector2 screenDir = new Vector2(direction.x, -direction.y);
        float angle = Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg;
        tiltConeImage.rectTransform.rotation = Quaternion.Euler(0f, 0f, angle);

        // Scale X wider than Y to create an asymmetric cone shape.
        float scale = Mathf.Lerp(0.8f, 1.4f, magnitude);
        tiltConeImage.transform.localScale = new Vector3(scale * 1.5f, scale, 1f);
        SetImageAlpha(tiltConeImage, Mathf.Lerp(0.3f, 0.8f, magnitude));

        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.TiltAimed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PINCH DELTA — zoom level indicator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Responds to a two-finger pinch or spread delta by showing and updating
    /// the zoom level indicator label.
    /// Cancels and restarts the fade coroutine on each pinch event so the
    /// indicator stays visible while pinching and only begins fading once
    /// the gesture ends and no new deltas arrive.
    /// Positive delta = fingers spreading (zoom in).
    /// Negative delta = fingers pinching (zoom out).
    /// Distinct ZoomIn / ZoomOut feedback types let FeedbackService play
    /// directionally appropriate audio when clips are assigned.
    /// </summary>
    private void HandlePinchDelta(float delta)
    {
        if (zoomIndicatorImage == null) return;

        SetImageAlpha(zoomIndicatorImage, 0.85f);

        if (zoomLevelText != null)
            zoomLevelText.text = delta > 0 ? "  zoom in" : "zoom out";

        if (_zoomFadeCoroutine != null)
            StopCoroutine(_zoomFadeCoroutine);

        _zoomFadeCoroutine = StartCoroutine(FadeZoomIndicator());

        // Positive delta = spread (zoom in), negative = pinch (zoom out).
        FeedbackService.Instance?.Play(delta > 0
            ? FeedbackService.FeedbackType.ZoomIn
            : FeedbackService.FeedbackType.ZoomOut);
    }

    private IEnumerator FadeZoomIndicator()
    {
        yield return new WaitForSeconds(zoomFadeDelay);

        float elapsed = 0f;
        while (elapsed < zoomFadeDuration)
        {
            SetImageAlpha(zoomIndicatorImage, Mathf.Lerp(0.85f, 0f, elapsed / zoomFadeDuration));
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetImageAlpha(zoomIndicatorImage, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SWIPE — movement trail stub
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stub — movement trail visual. Currently a no-op.
    /// PlayerVisuals.cs will own the trail renderer once built.
    /// This subscription is kept here to confirm the event is being received.
    /// Replace with a Trail Renderer enable/disable call once PlayerVisuals exists.
    /// </summary>
    private void HandleSwipe(Vector2 delta)
    {
        // TODO: enable PlayerVisuals trail renderer here when PlayerController is built.
        // Example: if (_playerVisuals != null) _playerVisuals.SetTrailActive(delta.magnitude > 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EASING FUNCTIONS
    // ─────────────────────────────────────────────────────────────────────────

    private static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private static float EaseInCubic(float t) => t * t * t;
    private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
    private static float EaseInQuad(float t) => t * t;

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the alpha of an Image's color without touching RGB channels.
    /// Safe no-op when the image reference is null.
    /// </summary>
    private static void SetImageAlpha(Image image, float alpha)
    {
        if (image == null) return;
        Color c = image.color;
        c.a = alpha;
        image.color = c;
    }
}