# Drift — Architecture Overview

## System Map

```
┌──────────────────────────────────────────────────────────────┐
│                        INPUT LAYER                            │
│  InputManager.cs                                              │
│  · Reads EnhancedTouch API  (sole consumer)                  │
│  · Reads Accelerometer.current (New Input System)            │
│  · Classifies: tap (1/2/3), swipe, pinch, hold,             │
│                finger count (1-5), tilt                       │
│  · Fires C# events via GestureEvents.cs                     │
│  · Nothing else reads hardware input                         │
└─────────────────────┬────────────────────────────────────────┘
                      │ events
          ┌───────────┼────────────┐
          ▼           ▼            ▼
┌──────────────────┐ ┌──────────┐ ┌─────────────────┐
│ PlayerController │ │UIFeedback│ │ GestureDebugUI  │
│ (NetworkBehaviour│ │(cosmetic)│ │ (dev overlay)   │
└────────┬─────────┘ └──────────┘ └─────────────────┘
         │ NetworkVariable / RPC
         ▼
┌──────────────────────────────────────────────────────────────┐
│                      NETCODE LAYER                            │
│  NetworkManager (Unity built-in)                             │
│  RelayManager.cs  ←→  Unity Relay (UGS)                     │
│  LobbyManager.cs  — join code flow                          │
│  NetworkPlayer.cs — per-player networked state               │
└─────────────────────┬────────────────────────────────────────┘
                      │
                      ▼
┌──────────────────────────────────────────────────────────────┐
│               WORLD LAYER (server-authoritative)              │
│  WorldManager.cs   — orb spawning, domain state              │
│  ResourceOrb.cs    — NetworkObject, collect RPC              │
│  DepositStation.cs — NetworkVariable<int> progress           │
└──────────────────────────────────────────────────────────────┘
```

---

## Script Responsibilities

### Core/
- **GameManager.cs** — singleton, game phase state machine (Lobby → Playing), wires system references

### Input/
- **InputManager.cs** — sole file reading touch (EnhancedTouch API) and accelerometer (UnityEngine.InputSystem.Accelerometer.current). 
      Fully migrated to New Input System. Handles tap timing and disambiguation, swipe detection, pinch, hold duration, 
      finger count classification, and tilt. Fires all events each frame via `GestureEvents`.
- **GestureEvents.cs** — static C# event definitions. The public contract every system depends on. All touch, tap, and tilt event signatures live here and nowhere else.

### Player/
- **PlayerController.cs** — `NetworkBehaviour`. Subscribes to GestureEvents, drives movement (swipe), dash (double tap), precision collect (single tap), tilt drift, and ability triggers. Networked position sync.
- **PlayerVisuals.cs** — purely cosmetic trail, color, animations. No game logic.

### Netcode/
- **RelayManager.cs** — allocates Relay server, generates join code, connects via join code
- **LobbyManager.cs** — wraps UGS Lobby API for join code UI flow
- **NetworkPlayer.cs** — per-player `NetworkVariable` state: resource count, player color

### World/
- **WorldManager.cs** — server-side: spawns orbs on timer, manages domain unlock state
- **ResourceOrb.cs** — `NetworkObject`. Handles `[ServerRpc]` collect request from both tap and pulse, despawn on confirm.
- **DepositStation.cs** — `NetworkObject`. `NetworkVariable<int>` deposit progress, domain unlock trigger.

### UI/
- **UIFeedback.cs** — subscribes to GestureEvents, drives all visual feedback: tap pop, dash flare, radial fill, ring animations, beam, domain pulse, tilt arrow, aimed cone
- **HUD.cs** — resource count, partner status, domain progress bars

---

## Full Gesture Event Contract

```csharp
// GestureEvents.cs — single source of truth for all input events
public static class GestureEvents
{
    // ── Tap Events ─────────────────────────────────────────────────

    /// Fired on confirmed single tap (after disambiguation delay).
    /// Position is world-space touch point.
    public static event Action<Vector2> OnSingleTap;

    /// Fired on confirmed double tap within the timing window.
    public static event Action OnDoubleTap;

    /// Fired on triple tap — currently stubbed, no systems subscribe.
    /// Reserved for future chest / interactable interaction.
    public static event Action OnTripleTap;

    // ── Swipe / Movement ───────────────────────────────────────────

    /// Fired each frame a single-finger swipe is active.
    /// Delta is in screen pixels per frame.
    public static event Action<Vector2> OnSwipe;

    // ── Pinch ──────────────────────────────────────────────────────

    /// Fired each frame a two-finger pinch is active.
    /// Positive = spread/zoom in, negative = pinch/zoom out.
    public static event Action<float> OnPinchDelta;

    // ── Hold / Pulse ───────────────────────────────────────────────

    /// Fired each frame while a hold-pulse is charging.
    /// Value 0-1 represents fill progress toward OnHoldComplete.
    public static event Action<float> OnHoldProgress;

    /// Fired once when hold charge reaches 1.0 (full charge).
    public static event Action OnHoldComplete;

    // ── Multi-Finger ───────────────────────────────────────────────

    /// Fired whenever the active touch point count changes.
    /// Value 1-5.
    public static event Action<int> OnFingerCountChanged;

    /// Fired each frame while exactly 3 fingers are held.
    /// Triggers triple-radius burst collect.
    public static event Action OnThreeFingerHold;

    /// Fired each frame while exactly 4 fingers are held.
    /// Triggers resource share beam toward nearest ally.
    public static event Action OnFourFingerHold;

    /// Fired each frame while 5 fingers are splayed and held.
    /// Triggers domain expand pulse.
    public static event Action OnFiveFingerSplay;

    // ── Tilt ───────────────────────────────────────────────────────

    /// Fired each frame with normalized X/Y tilt direction (-1 to 1).
    /// Drives ambient passive drift current. Deadzone applied.
    public static event Action<Vector2> OnTiltChanged;

    /// Fired each frame when tilt is active simultaneously with a hold-pulse.
    /// Direction and magnitude used to offset the collect radius cone.
    public static event Action<Vector2, float> OnTiltAimed;
}
```

---

## Tap Disambiguation Logic

Single, double, and triple tap share the same touch-begin event, so InputManager must disambiguate them using a short timing window:

```
Touch begins
    └─► Start tap timer (~0.2s window)
            │
            ├─ Another touch begins within window?
            │       └─► Increment tap count, reset timer
            │
            └─ Timer expires with no new touch?
                    ├─ count == 1 → fire OnSingleTap
                    ├─ count == 2 → fire OnDoubleTap
                    └─ count == 3 → fire OnTripleTap (stubbed)

Note: If finger moves beyond swipe threshold during window,
      classify as OnSwipe and discard tap count.
```

This means single tap has a ~0.2s delay before firing — acceptable for precision collect, and invisible during normal swipe-based movement.

---

## Tilt System Detail

```
Accelerometer.current.acceleration.ReadValue() (New Input System)        │
        ▼
InputManager.Update()
  · Extract X/Y axes, normalize to -1..1
  · Apply deadzone (magnitude < 0.1 → zero vector)
  · Fire OnTiltChanged(Vector2 tilt)
  · If hold-pulse is currently charging:
        └─► Fire OnTiltAimed(tilt, magnitude)
        │
        ├──► PlayerController
        │       OnTiltChanged  → gentle ambient force in lean direction
        │       OnTiltAimed    → offset collect radius cone direction
        │
        └──► UIFeedback
                OnTiltChanged  → show directional arrow at player edge
                OnTiltAimed    → draw asymmetric cone on pulse circle
```

---

## Networking Topology

```
[Host / Server]            [Client]
       │                       │
       │ ◄─── Join Code ───────│   (Unity Relay broker)
       │                       │
       │ ◄─── Collect RPC ─────│   (tap or pulse collect request)
       │                       │
       │ ──── Despawn Orb ─────►│   (server authoritative)
       │                       │
       │ ──── NetworkVar ──────►│   (station progress sync)
       │                       │
       │ ◄─── Position Sync ───│   (client owns own transform)
```

Host runs server logic. Both players can host. No dedicated server needed for a 2-player casual game.
