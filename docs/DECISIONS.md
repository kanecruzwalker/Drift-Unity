# Architecture Decisions — Drift

> This file logs key technical decisions made during development.
> Each entry explains the context, the options considered, and why we chose what we chose.
> Written for an engineering audience — useful for onboarding.

---

## ADR-001: Input Abstraction via Event System

**Date:** Project start
**Status:** Accepted

**Context:**
Mobile touch input in Unity can be accessed from anywhere via `Input.touches`. On a small project it's tempting to read touches directly in PlayerController or wherever needed. However this creates tight coupling between input hardware and game logic, makes testing harder, and means gesture logic gets scattered across files.

**Decision:**
All touch and tilt input is handled exclusively in `InputManager.cs`. It reads `Input.touches` and `Input.acceleration`, classifies all gestures, and fires C# events defined in `GestureEvents.cs`. Every other system subscribes to those events — nothing else ever reads hardware input directly.

**Alternatives considered:**
- Direct input reads in PlayerController — rejected, scatters gesture logic and creates tight coupling
- Unity's new Input System package — considered, but adds setup overhead; legacy touch API is sufficient for our gesture set

**Consequences:**
InputManager becomes the single source of truth for all input state. Adding a new gesture means editing one file. Swapping input backends later means replacing one file. GestureEvents.cs provides a clean public contract every system depends on.

---

## ADR-002: Unity Relay over Direct Connect for Multiplayer

**Date:** Project start
**Status:** Accepted

**Context:**
Netcode for GameObjects supports direct IP connect, which requires players to be on the same network or deal with port forwarding. For a casual game meant to be played with friends across different networks, that's a real barrier.

**Decision:**
Use Unity Relay (part of Unity Gaming Services free tier). The host allocates a Relay server slot and gets a 6-character join code. The client enters the code. No IP addresses, no port forwarding, works across mobile networks.

**Alternatives considered:**
- Direct connect — rejected, poor UX for casual play across networks
- Photon PUN — capable but adds a third-party dependency; UGS is already configured from a prior project
- Steam Networking — not appropriate for mobile

**Consequences:**
Requires UGS project configured with Relay enabled. Adds ~100ms latency versus direct connect, acceptable for a casual resource-collection game. Join code flow is simple enough to fit on screen without a complex lobby UI.

---

## ADR-003: Single Scene Architecture

**Date:** Project start
**Status:** Accepted

**Context:**
Scene transitions in networked games require careful handling — synchronized loads across all clients, object persistence management, and edge cases when players join mid-transition all adding complexity.

**Decision:**
One scene for the entire game. A lightweight state machine in `GameManager` handles lobby → playing transitions by showing/hiding UI panels and activating/deactivating game systems. No `LoadScene` calls after initial load.

**Consequences:**
Simpler Netcode setup. All NetworkObjects are always loaded. GameManager becomes the authority on game phase. Acceptable tradeoff for project scope.

---

## ADR-004: Server-Authoritative Orb Spawning

**Date:** Project start
**Status:** Accepted

**Context:**
In a cooperative game where both players collect shared orbs, if each client spawned and despawned independently you'd get desyncs — both players collecting the same orb, orbs appearing and disappearing inconsistently.

**Decision:**
Only the server (host) spawns and despawns `ResourceOrb` NetworkObjects. Clients send a collect request RPC; the server validates, despawns the orb, and updates the relevant `NetworkVariable` with the new resource count.

**Alternatives considered:**
- Client-authoritative collection — rejected due to desync risk in a shared-resource model
- Prediction + rollback — overcomplicated for a casual game at this scope

**Consequences:**
Collection feels slightly input-laggy on the client (round trip before despawn). Mitigated with client-side visual feedback — orb "grabbed" animation plays immediately on touch, despawns when server confirms. Standard pattern in authoritative multiplayer.

---

## ADR-005: Dual-Behavior Accelerometer Tilt

**Date:** Project start
**Status:** Accepted

**Context:**
The professor flagged tilt as a strong mobile-specific input. Two distinct behaviors were identified: ambient world drift (always active) and aimed collection radius (active during hold-pulse). The question was whether to implement one or both.

**Decision:**
Implement both as a layered tilt system within `InputManager`:

1. **Ambient drift current** — `Input.acceleration` X/Y values read every frame, mapped to gentle passive movement force. Always active. Fires `OnTiltChanged(Vector2)`.
2. **Aimed collect cone** — when a hold-pulse gesture is simultaneously active, tilt direction offsets the collection radius so it extends further in the lean direction. Fires `OnTiltAimed(Vector2 direction, float magnitude)`.

Both behaviors share the same underlying accelerometer read — no duplicate hardware polling.

**Alternatives considered:**
- Tilt only for ambient drift — simpler, but misses the expressive aiming potential
- Tilt only for aiming — less visible during normal play, weaker demo presence
- Gyroscope instead of accelerometer — more precise but harder to normalize across devices; accelerometer is sufficient for both behaviors

**Consequences:**
Tilt and swipe serve different roles so they don't conflict. Dual-behavior approach demonstrates both ambient and precision input in one gesture. `PlayerController` subscribes to both tilt events independently, keeping logic clean. A deadzone is applied to suppress jitter from a resting device.

---

## ADR-006: Tap Gesture Design and Triple-Tap Stub

**Date:** Project start
**Status:** Accepted

**Context:**
Single, double, and triple tap are distinct, recognizable mobile inputs the professor mentioned as valid. The question was whether all three have natural homes in the current gameplay loop, or whether adding them purely for input coverage would feel forced.

**Decision:**
Implement single and double tap as active gameplay inputs. Stub triple tap as a recognized-but-unwired event.

- **Single tap** → precision orb collect. Gives players a targeted alternative to the area-sweep hold-pulse. Feels natural and directly serves the core loop.
- **Double tap** → dash burst in current movement direction. Complements swipe movement with a speed burst option. Natural mobile pattern (double-tap to zoom is a familiar convention).
- **Triple tap** → `OnTripleTap` fires in `InputManager` and `GestureEvents` is defined, but no system subscribes to it. Reserved for chest/interactable interaction in a future development phase.

**Rationale for stubbing triple tap:**
Forcing an action onto triple tap with no natural gameplay fit would produce a demo where the input exists but feels arbitrary. Stubbing it is the honest engineering choice — it signals forward-thinking design rather than checkbox coverage. The event contract is in place so wiring it up later requires no InputManager changes.

**Consequences:**
Nine inputs are fully active. Triple tap is present in the event system, documented as reserved, and costs nothing to leave dormant. Future chest/interactable work can wire it up without touching the input layer.

---
