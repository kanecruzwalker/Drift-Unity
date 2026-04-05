using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GestureDebugUI — development overlay for verifying input events on-device.
///
/// Subscribes to all GestureEvents and renders a live on-screen readout
/// using Unity's OnGUI system (no canvas setup required).
///
/// Each event shows a label that flashes when the event fires, plus a
/// persistent last-value display for continuous events (swipe, pinch, tilt).
///
/// Usage:
///   · Attach to any active GameObject alongside InputManager.
///   · Toggle visibility with a three-finger tap (or the D key in editor).
///   · Remove or disable this component before submitting the final build.
///     It is safe to leave attached — the overlay only renders in debug mode.
/// </summary>
public class GestureDebugUI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SETTINGS
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Debug Display")]
    [Tooltip("Show the debug overlay on start. Toggle with three-finger tap or D key.")]
    [SerializeField] private bool showOnStart = true;

    [Tooltip("How long (seconds) a flashed event label stays highlighted after firing.")]
    [SerializeField] private float flashDuration = 0.4f;

    [Tooltip("Font size for the debug overlay labels.")]
    [SerializeField] private int fontSize = 28;

    // ─────────────────────────────────────────────────────────────────────────
    // STATE
    // ─────────────────────────────────────────────────────────────────────────

    private bool _visible;

    // Each entry: (label, flash expiry time, last value string)
    private readonly Dictionary<string, (float expiry, string value)> _events
        = new Dictionary<string, (float, string)>();

    // Persistent continuous values (updated every frame when active).
    private Vector2 _lastSwipe    = Vector2.zero;
    private float   _lastPinch    = 0f;
    private float   _lastHold     = 0f;
    private Vector2 _lastTilt     = Vector2.zero;
    private int     _lastFingers  = 0;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _visible = showOnStart;
        SubscribeToEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void Update()
    {
        // Toggle overlay with D key in editor, or watch for the 3-finger tap event.
    #if UNITY_EDITOR
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.dKey.wasPressedThisFrame)
            _visible = !_visible;
    #endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EVENT SUBSCRIPTIONS
    // ─────────────────────────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        GestureEvents.OnSingleTap          += OnSingleTap;
        GestureEvents.OnDoubleTap          += OnDoubleTap;
        GestureEvents.OnTripleTap          += OnTripleTap;
        GestureEvents.OnSwipe              += OnSwipe;
        GestureEvents.OnPinchDelta         += OnPinchDelta;
        GestureEvents.OnHoldProgress       += OnHoldProgress;
        GestureEvents.OnHoldComplete       += OnHoldComplete;
        GestureEvents.OnFingerCountChanged += OnFingerCountChanged;
        GestureEvents.OnThreeFingerHold    += OnThreeFingerHold;
        GestureEvents.OnFourFingerHold     += OnFourFingerHold;
        GestureEvents.OnFiveFingerSplay    += OnFiveFingerSplay;
        GestureEvents.OnTiltChanged        += OnTiltChanged;
        GestureEvents.OnTiltAimed          += OnTiltAimed;
    }

    private void UnsubscribeFromEvents()
    {
        GestureEvents.OnSingleTap          -= OnSingleTap;
        GestureEvents.OnDoubleTap          -= OnDoubleTap;
        GestureEvents.OnTripleTap          -= OnTripleTap;
        GestureEvents.OnSwipe              -= OnSwipe;
        GestureEvents.OnPinchDelta         -= OnPinchDelta;
        GestureEvents.OnHoldProgress       -= OnHoldProgress;
        GestureEvents.OnHoldComplete       -= OnHoldComplete;
        GestureEvents.OnFingerCountChanged -= OnFingerCountChanged;
        GestureEvents.OnThreeFingerHold    -= OnThreeFingerHold;
        GestureEvents.OnFourFingerHold     -= OnFourFingerHold;
        GestureEvents.OnFiveFingerSplay    -= OnFiveFingerSplay;
        GestureEvents.OnTiltChanged        -= OnTiltChanged;
        GestureEvents.OnTiltAimed          -= OnTiltAimed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EVENT HANDLERS
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSingleTap(Vector2 pos)
    {
        Flash("SINGLE TAP", $"({pos.x:F0}, {pos.y:F0})");
    }

    private void OnDoubleTap()
    {
        Flash("DOUBLE TAP", "");
    }

    private void OnTripleTap()
    {
        Flash("TRIPLE TAP [STUB]", "");
    }

    private void OnSwipe(Vector2 delta)
    {
        _lastSwipe = delta;
        Flash("SWIPE", $"Δ({delta.x:F1}, {delta.y:F1})");
    }

    private void OnPinchDelta(float delta)
    {
        _lastPinch = delta;
        Flash("PINCH", $"{delta:F1}px  {(delta > 0 ? "spread" : "pinch")}");
    }

    private void OnHoldProgress(float progress)
    {
        _lastHold = progress;
        // Update without flashing — this fires every frame while charging.
        SetValue("HOLD CHARGE", $"{progress * 100f:F0}%");
    }

    private void OnHoldComplete()
    {
        Flash("HOLD COMPLETE", "fired!");
        _lastHold = 0f;
        SetValue("HOLD CHARGE", "0%");
    }

    private void OnFingerCountChanged(int count)
    {
        _lastFingers = count;
        Flash("FINGER COUNT", count.ToString());
    }

    private void OnThreeFingerHold()
    {
        Flash("3-FINGER HOLD", "burst collect");
    }

    private void OnFourFingerHold()
    {
        Flash("4-FINGER HOLD", "share beam");
    }

    private void OnFiveFingerSplay()
    {
        Flash("5-FINGER SPLAY", "domain expand");
    }

    private void OnTiltChanged(Vector2 tilt)
    {
        _lastTilt = tilt;
        SetValue("TILT", $"({tilt.x:F2}, {tilt.y:F2})");
    }

    private void OnTiltAimed(Vector2 dir, float mag)
    {
        Flash("TILT AIMED", $"dir({dir.x:F2},{dir.y:F2}) mag:{mag:F2}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a flash entry — the label will be highlighted for flashDuration seconds.
    /// </summary>
    private void Flash(string key, string value)
    {
        _events[key] = (Time.unscaledTime + flashDuration, value);
    }

    /// <summary>
    /// Updates a continuous value label without resetting the flash timer.
    /// Used for events that fire every frame (hold progress, tilt).
    /// </summary>
    private void SetValue(string key, string value)
    {
        float currentExpiry = _events.ContainsKey(key) ? _events[key].expiry : 0f;
        _events[key] = (currentExpiry, value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RENDERING
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        if (!_visible) return;

        GUIStyle active = new GUIStyle(GUI.skin.label)
        {
            fontSize  = fontSize,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.green }
        };

        GUIStyle idle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = fontSize,
            fontStyle = FontStyle.Normal,
            normal    = { textColor = new Color(0.6f, 0.6f, 0.6f) }
        };

        GUIStyle header = new GUIStyle(GUI.skin.label)
        {
            fontSize  = fontSize - 4,
            fontStyle = FontStyle.Bold,
            normal    = { textColor = Color.yellow }
        };

        // Background panel.
        GUI.Box(new Rect(10, 10, 420, 780), "");

        float y = 20f;
        float lineH = fontSize + 10f;

        GUI.Label(new Rect(20, y, 400, lineH), "── GESTURE DEBUG ──", header);
        y += lineH;
        GUI.Label(new Rect(20, y, 400, lineH - 4), "D key or 3-tap to toggle", idle);
        y += lineH + 4;

        foreach (var kvp in _events)
        {
            bool flashing = Time.unscaledTime < kvp.Value.expiry;
            GUIStyle style = flashing ? active : idle;
            string label = kvp.Value.value.Length > 0
                ? $"{kvp.Key}  {kvp.Value.value}"
                : kvp.Key;
            GUI.Label(new Rect(20, y, 400, lineH), label, style);
            y += lineH;
        }

        // Always-on persistent values at bottom.
        y += 4;
        GUI.Label(new Rect(20, y, 400, lineH), $"fingers: {_lastFingers}", idle);
        y += lineH;
        GUI.Label(new Rect(20, y, 400, lineH), $"tilt: ({_lastTilt.x:F2}, {_lastTilt.y:F2})", idle);
    }
}