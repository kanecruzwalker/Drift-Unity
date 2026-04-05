using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// InputManager — the sole consumer of touch and accelerometer input in Drift.
///
/// Migrated to Unity's New Input System (EnhancedTouch API).
/// EnhancedTouchSupport must be enabled before Touch.activeTouches is accessible —
/// this is done in Awake/OnEnable and torn down in OnDisable.
///
/// Responsibilities:
///   · Read all hardware touch and tilt input every frame
///   · Classify gestures: tap (1/2/3), swipe, pinch, hold-pulse, 3/4/5-finger, tilt
///   · Fire corresponding events via GestureEvents invoke helpers
///   · Apply disambiguation and debounce so events are clean and unambiguous
///
/// What this class does NOT do:
///   · Move the player (PlayerController subscribes to OnSwipe / OnTiltChanged)
///   · Show any feedback (UIFeedback subscribes to hold/tap/multi events)
///   · Know anything about game state
///
/// Attach to a persistent GameObject in the scene (e.g. the GameManager object).
/// There should be exactly one InputManager in the scene.
/// </summary>
public class InputManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // TUNING CONSTANTS
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Tap Settings")]
    [Tooltip("Time window (seconds) to accumulate taps before firing the tap event.")]
    [SerializeField] private float tapWindow = 0.2f;

    [Tooltip("Maximum finger movement (screen pixels) before a touch is reclassified as a swipe.")]
    [SerializeField] private float tapMoveThreshold = 20f;

    [Header("Hold Settings")]
    [Tooltip("Time (seconds) a two-finger hold must be sustained to reach full charge.")]
    [SerializeField] private float holdChargeTime = 1.5f;

    [Tooltip("Minimum time (seconds) a two-finger hold must be held before charge starts.")]
    [SerializeField] private float holdStartDelay = 0.1f;

    [Header("Tilt Settings")]
    [Tooltip("Accelerometer magnitude below which tilt is treated as zero (suppresses resting jitter).")]
    [SerializeField] private float tiltDeadzone = 0.1f;

    [Tooltip("Multiplier applied to raw accelerometer values before clamping to -1..1.")]
    [SerializeField] private float tiltSensitivity = 2.0f;

    // ─────────────────────────────────────────────────────────────────────────
    // TAP STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Number of taps accumulated in the current timing window.
    private int _tapCount = 0;

    /// Time remaining before the current tap window closes and the event fires.
    private float _tapTimer = 0f;

    /// Whether we are currently inside an open tap timing window.
    private bool _tapWindowOpen = false;

    /// Screen position of the first tap in this window (used for OnSingleTap position).
    private Vector2 _tapStartPosition = Vector2.zero;

    /// Whether the current touch has moved enough to be reclassified as a swipe.
    private bool _tapCancelledByMove = false;

    // ─────────────────────────────────────────────────────────────────────────
    // SWIPE STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Whether a single-finger swipe is currently active.
    private bool _swipeActive = false;

    /// Previous frame position of the swiping finger (for delta calculation).
    private Vector2 _swipePrevPosition = Vector2.zero;

    // ─────────────────────────────────────────────────────────────────────────
    // PINCH STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Distance between the two pinch fingers last frame (for delta calculation).
    private float _pinchPrevDistance = 0f;

    // ─────────────────────────────────────────────────────────────────────────
    // HOLD / PULSE STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Accumulated hold time for the current two-finger hold gesture.
    private float _holdElapsed = 0f;

    /// Whether a two-finger hold is currently active and charging.
    private bool _holdActive = false;

    /// Whether the hold has already fired OnHoldComplete this gesture cycle.
    private bool _holdFired = false;

    /// Whether the hold was confirmed stationary long enough to lock out pinch.
    /// Once set, requires _holdPinchOverrideThreshold movement to break back to pinch.
    private bool _holdLocked = false;

    /// How long (seconds) both fingers must remain stationary before hold locks out pinch.
    private const float HoldLockDelay = 0.15f;

    /// Accumulated stationary time used to reach HoldLockDelay.
    private float _holdLockTimer = 0f;

    /// Movement magnitude (screen pixels) required to break a locked hold back to pinch.
    /// Higher than the initial pinch threshold to create hysteresis.
    private const float HoldBreakThreshold = 12f;

    // ─────────────────────────────────────────────────────────────────────────
    // FINGER COUNT STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Finger count reported last frame — used to detect changes for OnFingerCountChanged.
    private int _prevFingerCount = 0;

    // ─────────────────────────────────────────────────────────────────────────
    // TILT CALIBRATION STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// The accelerometer reading captured at calibration time.
    /// All tilt events are reported as deltas from this baseline.
    /// Initialized to Vector3.zero — CalibrateNeutral() should be called
    /// once the player is holding the device in their natural resting position.
    private Vector3 _tiltNeutral = Vector3.zero;

    /// Whether CalibrateNeutral() has been called at least once.
    /// Before calibration, tilt events use raw accelerometer values.
    private bool _tiltCalibrated = false;

    /// Rate at which the neutral baseline lerps toward current orientation each frame.
    /// Lower = slower self-correction (more stable), higher = faster (more responsive).
    /// 0.5 gives ~2s to fully re-center after a hold angle change.
    [Header("Tilt Settings")]  // add under existing tilt header in tuning section
    [Tooltip("Rate the tilt neutral baseline self-corrects toward current orientation (0=never, 1=instant).")]
    [SerializeField] private float tiltSmoothingRate = 0.5f;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        EnhancedTouchSupport.Enable();

        // Enable accelerometer via New Input System
        if(UnityEngine.InputSystem.Accelerometer.current != null)
            UnityEngine.InputSystem.InputSystem.EnableDevice(UnityEngine.InputSystem.Accelerometer.current);

        Debug.Log("[Input] *** InputManager Awake — EnhancedTouch enabled, tilt calibration scheduled ***");
    }

    private void OnEnable()  { EnhancedTouchSupport.Enable(); }
    private void OnDisable() { EnhancedTouchSupport.Disable(); }

    private void Update()
    {
        int fingerCount = Touch.activeTouches.Count;

        // Debug log — remove before final submission.
        if (fingerCount > 0)
            Debug.Log($"[Input] {fingerCount} finger(s) — phase: {Touch.activeTouches[0].phase}");

        // Fire finger count change event when the active count changes.
        if (fingerCount != _prevFingerCount && fingerCount > 0)
            GestureEvents.InvokeFingerCountChanged(Mathf.Clamp(fingerCount, 1, 5));

        _prevFingerCount = fingerCount;

        // Route to gesture classifiers based on active finger count.
        switch (fingerCount)
        {
            case 0:
                HandleNoTouch();
                break;
            case 1:
                HandleSingleTouch(Touch.activeTouches[0]);
                break;
            case 2:
                HandleTwoTouches(Touch.activeTouches[0], Touch.activeTouches[1]);
                break;
            case 3:
                HandleThreeFingerHold();
                break;
            case 4:
                HandleFourFingerHold();
                break;
            default: // 5+
                HandleFiveFingerSplay();
                break;
        }

        TickTapWindow();
        HandleTilt();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NO TOUCH
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when no fingers are on screen. Cleans up any active gesture state.
    /// </summary>
    private void HandleNoTouch()
    {
        _swipeActive = false;
        ResetHoldState();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SINGLE TOUCH — tap disambiguation + swipe
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles a single finger on screen. Disambiguates between tap and swipe.
    ///
    /// Logic:
    ///   Began      → record start position, open tap window if not already open
    ///   Moved      → if movement exceeds tapMoveThreshold, cancel tap and activate swipe
    ///   Stationary → if swipe is active, fire OnSwipe with zero delta
    ///   Ended      → if tap was not cancelled, increment tap count
    /// </summary>
    private void HandleSingleTouch(Touch touch)
    {
        // Calibrate on the very first touch — device is in the player's hand by now.
        if (!_tiltCalibrated && touch.phase == TouchPhase.Began)
            CalibrateNeutral();

        ResetHoldState();

        switch (touch.phase)
        {
            case TouchPhase.Began:
                _tapCancelledByMove = false;
                _swipeActive = false;
                _swipePrevPosition = touch.screenPosition;

                if (!_tapWindowOpen)
                {
                    _tapStartPosition = touch.screenPosition;
                    _tapWindowOpen = true;
                    _tapTimer = tapWindow;
                    _tapCount = 0;
                }
                break;

            case TouchPhase.Moved:
                float moveDistance = Vector2.Distance(touch.screenPosition, _swipePrevPosition);

                if (!_tapCancelledByMove && moveDistance > tapMoveThreshold)
                {
                    _tapCancelledByMove = true;
                    _swipeActive = true;
                    _tapWindowOpen = false;
                    _tapCount = 0;
                }

                if (_swipeActive)
                {
                    Vector2 delta = touch.screenPosition - _swipePrevPosition;
                    GestureEvents.InvokeSwipe(delta);
                    _swipePrevPosition = touch.screenPosition;
                }
                break;

            case TouchPhase.Stationary:
                if (_swipeActive)
                    GestureEvents.InvokeSwipe(Vector2.zero);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                _swipeActive = false;

                if (!_tapCancelledByMove && _tapWindowOpen)
                {
                    _tapCount++;
                    _tapTimer = tapWindow;
                }
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TWO TOUCHES — pinch vs hold-pulse disambiguation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles exactly two fingers on screen.
    /// Disambiguates between pinch and hold-pulse using a hysteresis lock:
    ///
    ///   · If fingers are clearly moving on first contact → pinch.
    ///   · If fingers settle stationary for HoldLockDelay seconds → hold locks in.
    ///   · Once hold is locked, only movement exceeding HoldBreakThreshold
    ///     can break it back to pinch. This prevents micro-movement during
    ///     a deliberate hold from accidentally triggering pinch.
    ///
    /// This solves the common issue where settling fingers slightly shift position
    /// as the user tries to hold — without this guard, those micro-movements
    /// immediately hijack the gesture as a pinch.
    /// </summary>
    private void HandleTwoTouches(Touch t0, Touch t1)
    {
        _swipeActive = false;
        _tapWindowOpen = false;
        _tapCount = 0;

        float currentDistance = Vector2.Distance(t0.screenPosition, t1.screenPosition);
        float t0Move = t0.delta.magnitude;
        float t1Move = t1.delta.magnitude;
        float maxMove = Mathf.Max(t0Move, t1Move);

        bool touchJustBegan = t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began;

        if (touchJustBegan)
        {
            // Fresh contact — reset disambiguation state.
            _pinchPrevDistance = currentDistance;
            _holdLocked = false;
            _holdLockTimer = 0f;
            ResetHoldState();
            return;
        }

        // ── Hold lock acquisition ─────────────────────────────────────────────
        // Fingers stationary → accumulate lock timer toward HoldLockDelay.
        // Once locked, hold wins unless fingers move beyond HoldBreakThreshold.

        bool fingersSettled = maxMove <= 2f;

        if (!_holdLocked)
        {
            if (fingersSettled)
            {
                _holdLockTimer += Time.deltaTime;
                if (_holdLockTimer >= HoldLockDelay)
                    _holdLocked = true;
            }
            else
            {
                // Movement before lock — treat as pinch, reset lock timer.
                _holdLockTimer = 0f;
                ResetHoldState();

                float pinchDelta = currentDistance - _pinchPrevDistance;
                if (Mathf.Abs(pinchDelta) > 0.5f)
                    GestureEvents.InvokePinchDelta(pinchDelta);
                _pinchPrevDistance = currentDistance;
                return;
            }
        }
        else
        {
            // Hold is locked — only break if movement exceeds HoldBreakThreshold.
            if (maxMove > HoldBreakThreshold)
            {
                _holdLocked = false;
                _holdLockTimer = 0f;
                ResetHoldState();

                float pinchDelta = currentDistance - _pinchPrevDistance;
                if (Mathf.Abs(pinchDelta) > 0.5f)
                    GestureEvents.InvokePinchDelta(pinchDelta);
                _pinchPrevDistance = currentDistance;
                return;
            }
        }

        // ── Hold charge (lock confirmed or acquiring) ─────────────────────────
        _pinchPrevDistance = currentDistance; // Keep distance current in case lock breaks.

        if (!_holdActive)
        {
            _holdActive = true;
            _holdElapsed = 0f;
            _holdFired = false;
        }

        if (!_holdFired)
        {
            _holdElapsed += Time.deltaTime;
            float chargeProgress = Mathf.Clamp01((_holdElapsed - holdStartDelay) / holdChargeTime);

            if (chargeProgress > 0f)
                GestureEvents.InvokeHoldProgress(chargeProgress);

            if (chargeProgress >= 1f)
            {
                _holdFired = true;
                GestureEvents.InvokeHoldComplete();
                ResetHoldState();
            }
        }
    }
    // ─────────────────────────────────────────────────────────────────────────
    // MULTI-FINGER (3 / 4 / 5)
    // ─────────────────────────────────────────────────────────────────────────

    /// Three fingers held — burst collect ability.
    private void HandleThreeFingerHold()
    {
        ResetHoldState();
        GestureEvents.InvokeThreeFingerHold();
    }

    /// Four fingers held — resource share beam toward nearest ally.
    private void HandleFourFingerHold()
    {
        ResetHoldState();
        GestureEvents.InvokeFourFingerHold();
    }

    /// Five or more fingers held — domain expand pulse.
    private void HandleFiveFingerSplay()
    {
        ResetHoldState();
        GestureEvents.InvokeFiveFingerSplay();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TAP WINDOW TICKER
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Counts down the tap window timer each frame.
    /// When expired: 1 tap → OnSingleTap, 2 → OnDoubleTap, 3 → OnTripleTap (stubbed).
    /// </summary>
    private void TickTapWindow()
    {
        if (!_tapWindowOpen) return;

        _tapTimer -= Time.deltaTime;

        if (_tapTimer <= 0f)
        {
            _tapWindowOpen = false;

            switch (_tapCount)
            {
                case 1:
                    GestureEvents.InvokeSingleTap(_tapStartPosition);
                    break;
                case 2:
                    GestureEvents.InvokeDoubleTap();
                    break;
                default:
                    GestureEvents.InvokeTripleTap();
                    break;
            }

            _tapCount = 0;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TILT CALIBRATION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Snaps the tilt neutral baseline to the current accelerometer reading.
    /// Called automatically on the player's first touch (TouchPhase.Began in
    /// HandleSingleTouch) — the device is definitively in hand at that moment.
    ///
    /// Auto-calibrate via Invoke(delay) was unreliable on-device: the delay
    /// sometimes fired before the player picked up the phone, capturing a flat
    /// baseline instead of a held one. First-touch calibration is deterministic.
    ///
    /// After this call, HandleTilt() reports all tilt as delta from this baseline,
    /// and the continuous low-pass filter in HandleTilt() gradually self-corrects
    /// the baseline as the player shifts their hold angle during play.
    ///
    /// Can also be called from a UI "recalibrate" button for explicit reset.
    /// Fires FeedbackService.Calibrated for audio/haptic confirmation.
    /// See ADR-008 for full calibration strategy history.
    /// </summary>


    public void CalibrateNeutral()
    {
        var accelerometer = UnityEngine.InputSystem.Accelerometer.current;
        if (accelerometer == null) return;

        _tiltNeutral = accelerometer.acceleration.ReadValue();
        _tiltCalibrated = true;

        Debug.Log($"[Input] Tilt calibrated. Neutral baseline: {_tiltNeutral}");
        FeedbackService.Instance?.Play(FeedbackService.FeedbackType.Calibrated);

    }


    // ─────────────────────────────────────────────────────────────────────────
    // TILT
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads accelerometer input every frame and fires tilt events.
    /// Uses UnityEngine.InputSystem.Accelerometer.current (New Input System).
    ///
    /// Axis mapping — Android portrait orientation:
    ///   accel.x = left/right roll — correct as-is, no adjustment needed
    ///   accel.y = forward/back pitch — NEGATED: on Android portrait, Y increases
    ///             when bottom tilts away (opposite of screen-space up convention).
    ///             Negating delta.y makes top-tilt-away = positive Y = up arrow.
    ///   accel.z = face-up/face-down gravity component — not used for tilt direction
    ///
    /// Calibration strategy (three-layer system, arrived at through device testing):
    ///
    ///   Layer 1 — Snap on first touch (CalibrateNeutral):
    ///     Called on TouchPhase.Began of the first touch. Device is definitively
    ///     in the player's hand at this point. Auto-calibrate via Invoke(0.5f) was
    ///     unreliable — sometimes fired while phone was still on the desk.
    ///
    ///   Layer 2 — Continuous low-pass self-correction:
    ///     Every frame, _tiltNeutral lerps toward the current accel reading at
    ///     tiltSmoothingRate. This self-corrects baseline drift as the player
    ///     shifts their hold angle during play — no manual recalibration needed.
    ///     Rate 0.5 ≈ 2s to re-center. Standard approach for casual mobile games.
    ///
    ///   Layer 3 — Deadzone:
    ///     Magnitude below tiltDeadzone treated as zero. Suppresses resting jitter
    ///     from the accelerometer when the device is held still.
    ///
    /// Fires OnTiltChanged every frame (zero vector when within deadzone).
    /// Fires OnTiltAimed when tilt is active simultaneously with a hold-pulse charge.
    /// See ADR-008 for full discovery history.
    /// </summary>
    private void HandleTilt()
    {
        var accelerometer = UnityEngine.InputSystem.Accelerometer.current;
        if (accelerometer == null) return;

        Vector3 accel = accelerometer.acceleration.ReadValue();

        // ── Layer 2: Continuous low-pass baseline self-correction ─────────────
        // Slowly tracks current orientation so baseline re-centers after the
        // player shifts their hold angle. CalibrateNeutral() snaps instantly
        // on first touch; this filter self-corrects gradually during play.
        if (_tiltCalibrated)
            _tiltNeutral = Vector3.Lerp(_tiltNeutral, accel, tiltSmoothingRate * Time.deltaTime);

        // ── Compute delta from calibrated baseline ────────────────────────────
        Vector3 delta = _tiltCalibrated ? accel - _tiltNeutral : accel;

        // ── Map to screen-space tilt vector ───────────────────────────────────
        // X: left/right roll — no adjustment needed.
        // Y: negated — Android portrait Y axis is inverted vs screen-space up.
        //    Tilt top away (forward) → delta.y negative → -delta.y positive → up arrow. ✓
        Vector2 rawTilt = new Vector2(delta.x, -delta.y) * tiltSensitivity;
        rawTilt.x = Mathf.Clamp(rawTilt.x, -1f, 1f);
        rawTilt.y = Mathf.Clamp(rawTilt.y, -1f, 1f);

        // ── Layer 3: Deadzone ─────────────────────────────────────────────────
        float magnitude = rawTilt.magnitude;
        if (magnitude < tiltDeadzone)
        {
            rawTilt = Vector2.zero;
            magnitude = 0f;
        }
        else
        {
            magnitude = Mathf.InverseLerp(tiltDeadzone, 1f, magnitude);
        }

        GestureEvents.InvokeTiltChanged(rawTilt);

        // Fire aimed event when tilt is active during a hold-pulse charge.
        if (_holdActive && !_holdFired && magnitude > 0f)
        {
            Vector2 tiltDirection = rawTilt.magnitude > 0f ? rawTilt.normalized : Vector2.zero;
            GestureEvents.InvokeTiltAimed(tiltDirection, magnitude);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets all hold-pulse tracking state. Called whenever a gesture transition
    /// makes hold-pulse incompatible with the current touch configuration.
    /// Also resets the hysteresis lock timer so the next two-finger contact
    /// starts fresh disambiguation.
    /// </summary>
    private void ResetHoldState()
    {
        _holdActive = false;
        _holdElapsed = 0f;
        _holdFired = false;
        _holdLocked = false;
        _holdLockTimer = 0f;
    }
}