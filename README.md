# Drift — Cooperative Mobile Resource World

> A casual cooperative mobile game built in Unity 6 LTS with Netcode for GameObjects and Unity Relay.
> Players drift through a shared world, collecting orbs and depositing them at stations to expand their domain together.

---

## Input Reference

### Tap Gestures

| Gesture | Action | Feedback |
|---------|--------|----------|
| Single tap | Precision collect — grab a specific orb directly | Orb flash + collect pop |
| Double tap | Dash burst — speed boost in movement direction | Speed trail flare |
| Triple tap | *(Reserved — future interactable interaction)* | — |

### Touch — Multi-Finger System

| Fingers | Gesture | Action | Feedback |
|---------|---------|--------|----------|
| 1 | Swipe | Active movement | Player movement trail |
| 2 | Pinch / spread | Map zoom | Zoom level indicator |
| 2 | Hold | Charge collect pulse | Radial fill circle |
| 3 | Hold | Triple-radius burst collect | 3 orbiting rings expand |
| 4 | Hold | Share resources to partner | Beam toward nearest ally |
| 5 | Splay hold | Domain expand | Radial pulse from hand center |

### Accelerometer — Phone Tilt

| Tilt | Action | Feedback |
|------|--------|----------|
| Ambient lean | Passive drift current in lean direction | Directional arrow at player edge |
| Tilt during hold-pulse | Aimed collect — cone extends in lean direction | Asymmetric orb vacuum cone |

**Inputs are complementary, not redundant.** Tap targets specific orbs. Swipe drives active movement. Tilt creates ambient drift and collection aiming. Multi-finger holds trigger escalating cooperative abilities. All can be layered simultaneously.

---

## Multiplayer — How to Play Together

1. One player hosts — a 6-character join code appears on screen
2. Second player enters the code on their device
3. Both players appear in the same world in real time
4. Works across different networks — no shared Wi-Fi required

---

## Architecture Highlights

- **InputManager** is the sole consumer of touch (EnhancedTouch API) and accelerometer (New Input System) — fires C# events that all other systems subscribe to.-
- **GestureEvents** is the single source of truth for all event signatures — touch, tap, and tilt.
- **Unity Relay** handles NAT traversal — no server hosting or port forwarding needed.
- **Server-authoritative** orb spawning via `WorldManager` — no collection desyncs in multiplayer.
- **NetworkVariable** for shared station progress — both players always see the same world state.
- **Single scene architecture** — no networked scene transitions, simpler Netcode setup.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full system map and [`docs/DECISIONS.md`](docs/DECISIONS.md) for engineering rationale behind each major decision.

---

## Build Instructions

### Android (standalone — recommended)
1. Open project in Unity 6 LTS
2. File → Build Settings → Android → Switch Platform
3. Enable USB debugging on your Android device
4. Connect via USB → Build and Run

### Unity Remote (iPhone — development iteration only)
1. Install **Unity Remote 5** from the App Store
2. Connect iPhone via USB
3. Edit → Project Settings → Editor → Unity Remote → Device: Any iOS Device
4. Hit Play in the Unity Editor

---

## Project Structure

```
Assets/Scripts/
  Core/       — GameManager, constants
  Input/      — InputManager, GestureEvents (all touch, tap, tilt)
  Player/     — PlayerController, PlayerVisuals
  Netcode/    — RelayManager, LobbyManager, NetworkPlayer
  World/      — WorldManager, ResourceOrb, DepositStation
  UI/         — UIFeedback, GestureFeedbackUI, HUD
docs/
  ARCHITECTURE.md   — system map and script responsibilities
  DECISIONS.md      — architecture decision record log
  GIT_STRATEGY.md   — branch structure and commit conventions
  secrets-setup.md  — UGS configuration guide
```

---

## Assignment Context

Built for CS Advanced 3D Game Design — mobile input assignment.
Focus: 11-input system spanning tap gestures, 5-tier multi-finger touch, and dual-behavior accelerometer tilt — with cooperative multiplayer via Unity Relay.

**Platform:** Android (standalone APK) + Unity Remote (iPhone, development)
**Engine:** Unity 6 LTS
**Networking:** Netcode for GameObjects + Unity Gaming Services Relay
