using System;
using UnityEngine;

/// <summary>
/// Single source of truth for all gesture and tilt input events in Drift.
/// 
/// Nothing in the project reads Input.touches or Input.acceleration directly —
/// InputManager.cs is the sole hardware consumer. Every other system subscribes
/// to these events to react to input.
/// 
/// Event categories:
///   Tap     — single / double / triple (triple stubbed, reserved)
///   Swipe   — single-finger active movement
///   Pinch   — two-finger zoom
///   Hold    — press-and-hold charge pulse
///   Multi   — 3 / 4 / 5 finger cooperative abilities
///   Tilt    — accelerometer ambient drift + aimed collect cone
/// </summary>
public static class GestureEvents
{
    // ─────────────────────────────────────────────────────────────────────────
    // TAP EVENTS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired on a confirmed single tap after the disambiguation window expires.
    /// Position is screen-space pixel coordinates of the touch.
    /// Note: fires ~0.2s after the touch due to disambiguation delay — this is
    /// intentional and invisible during normal swipe-based play.
    /// </summary>
    public static event Action<Vector2> OnSingleTap;

    /// <summary>
    /// Fired on a confirmed double tap within the timing window.
    /// Triggers dash burst in the player's current movement direction.
    /// </summary>
    public static event Action OnDoubleTap;

    /// <summary>
    /// Fired on a triple tap — STUBBED. No systems subscribe to this event.
    /// The event contract is defined here so wiring it up later requires
    /// no changes to InputManager or GestureEvents. Reserved for future
    /// chest / interactable interaction. See ADR-006.
    /// </summary>
    public static event Action OnTripleTap;

    // ─────────────────────────────────────────────────────────────────────────
    // SWIPE / MOVEMENT
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired each frame a single-finger swipe is active.
    /// Value is screen-space delta in pixels since last frame.
    /// Consumed by PlayerController to drive active movement.
    /// </summary>
    public static event Action<Vector2> OnSwipe;

    // ─────────────────────────────────────────────────────────────────────────
    // PINCH
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired each frame a two-finger pinch or spread is active.
    /// Positive value = fingers spreading (zoom in).
    /// Negative value = fingers pinching (zoom out).
    /// Magnitude is the delta in screen pixels since last frame.
    /// </summary>
    public static event Action<float> OnPinchDelta;

    // ─────────────────────────────────────────────────────────────────────────
    // HOLD / PULSE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired each frame while a two-finger hold-pulse is charging.
    /// Value 0.0–1.0 represents fill progress toward full charge.
    /// Consumed by UIFeedback to drive the radial fill circle.
    /// </summary>
    public static event Action<float> OnHoldProgress;

    /// <summary>
    /// Fired once when a two-finger hold-pulse reaches full charge (progress == 1.0).
    /// Triggers the area collect vacuum. Resets hold state after firing.
    /// </summary>
    public static event Action OnHoldComplete;

    // ─────────────────────────────────────────────────────────────────────────
    // MULTI-FINGER
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired whenever the active touch point count changes.
    /// Value is clamped 1–5. Useful for UI that shows current finger count.
    /// </summary>
    public static event Action<int> OnFingerCountChanged;

    /// <summary>
    /// Fired each frame while exactly 3 fingers are held on screen.
    /// Triggers triple-radius burst collect — large area orb vacuum.
    /// </summary>
    public static event Action OnThreeFingerHold;

    /// <summary>
    /// Fired each frame while exactly 4 fingers are held on screen.
    /// Triggers resource share beam toward the nearest ally.
    /// </summary>
    public static event Action OnFourFingerHold;

    /// <summary>
    /// Fired each frame while 5 fingers are splayed and held on screen.
    /// Triggers domain expand pulse — unlocks surrounding world area.
    /// </summary>
    public static event Action OnFiveFingerSplay;

    // ─────────────────────────────────────────────────────────────────────────
    // TILT
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired each frame with the normalized tilt direction (-1 to 1 on each axis).
    /// X = left/right lean, Y = forward/back lean.
    /// Deadzone applied — zero vector when device is near-flat.
    /// Drives the ambient passive drift current on PlayerController.
    /// </summary>
    public static event Action<Vector2> OnTiltChanged;

    /// <summary>
    /// Fired each frame when tilt is active simultaneously with a hold-pulse charge.
    /// direction: normalized tilt vector (-1 to 1).
    /// magnitude: raw tilt strength after deadzone (0 to 1).
    /// Used to offset the collect radius cone in the lean direction.
    /// </summary>
    public static event Action<Vector2, float> OnTiltAimed;

    // ─────────────────────────────────────────────────────────────────────────
    // INTERNAL INVOKE HELPERS
    // Called only by InputManager. No other file should call these.
    // ─────────────────────────────────────────────────────────────────────────

    internal static void InvokeSingleTap(Vector2 position)       => OnSingleTap?.Invoke(position);
    internal static void InvokeDoubleTap()                        => OnDoubleTap?.Invoke();
    internal static void InvokeTripleTap()                        => OnTripleTap?.Invoke();
    internal static void InvokeSwipe(Vector2 delta)               => OnSwipe?.Invoke(delta);
    internal static void InvokePinchDelta(float delta)            => OnPinchDelta?.Invoke(delta);
    internal static void InvokeHoldProgress(float progress)       => OnHoldProgress?.Invoke(progress);
    internal static void InvokeHoldComplete()                     => OnHoldComplete?.Invoke();
    internal static void InvokeFingerCountChanged(int count)      => OnFingerCountChanged?.Invoke(count);
    internal static void InvokeThreeFingerHold()                  => OnThreeFingerHold?.Invoke();
    internal static void InvokeFourFingerHold()                   => OnFourFingerHold?.Invoke();
    internal static void InvokeFiveFingerSplay()                  => OnFiveFingerSplay?.Invoke();
    internal static void InvokeTiltChanged(Vector2 tilt)          => OnTiltChanged?.Invoke(tilt);
    internal static void InvokeTiltAimed(Vector2 dir, float mag)  => OnTiltAimed?.Invoke(dir, mag);
}