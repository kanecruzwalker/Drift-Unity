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
All touch and tilt input is handled exclusively in `InputManager.cs`. It reads touch via EnhancedTouch API and tilt via Accelerometer.current (New Input System), classifies all gestures, and fires C# events defined in `GestureEvents.cs`. Every other system subscribes to those events — nothing else ever reads hardware input directly.

**Alternatives considered:**
- Direct input reads in PlayerController — rejected, scatters gesture logic and creates tight coupling
- Unity's new Input System package — adopted mid-session after confirming legacy Input.touches 
  does not route touches on Android when New Input System package is active. Full migration to 
  EnhancedTouch API and Accelerometer.current completed before first device-verified build.

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
---

## ADR-007: UIFeedback Architecture — Separation of Canvas Ownership and Animation Logic

**Date:** feature/ui-feedback
**Status:** Accepted

**Context:**
Visual feedback requires two distinct concerns: (1) owning and configuring UI elements in the Canvas hierarchy, and (2) animating those elements in response to input events. Putting both in one script creates a class that is simultaneously a Unity scene setup tool and a per-frame animation driver — two very different responsibilities.

Additionally, feedback elements need to be auto-wired to UIFeedback's Inspector slots to avoid requiring manual Canvas setup before any input feedback works.

**Decision:**
Split into two scripts with a clear ownership contract:

- **GestureFeedbackUI.cs** — owns the Canvas hierarchy. Creates all Image elements procedurally at Awake (via `autoBuildCanvas`). Provides public properties for each element. Calls `AutoPopulateFeedback(UIFeedback target)` to inject references into UIFeedback via reflection, removing manual Inspector wiring as a hard requirement.
- **UIFeedback.cs** — owns all animation logic. Subscribes to GestureEvents. Drives every visual effect. Has no knowledge of how elements were created — it only knows their Image references.

**Player anchor strategy:**
UIFeedback resolves the player screen position via `FindWithTag("Player")` each frame with a screen-center fallback. This makes UIFeedback functional before PlayerController is built and auto-connects at runtime once a player exists — zero manual wiring needed at any project stage.

**Four-finger beam placeholder:**
The four-finger share beam requires a networked partner position (from NetworkPlayer / NetworkVariable) which does not yet exist. Rather than drawing a beam toward an arbitrary screen position, the four-finger gesture shows a pulsing "searching" ring at the player anchor. This is honest about the system state and requires no cleanup when the real beam is wired. The actual beam direction will be subscribed from NetworkPlayer's partner position in feature/netcode-foundation.

**Particle system hooks:**
All particle effects are optional Inspector slots with safe null-guards. Logic always runs; particles play if assigned. This means the game has full functional feedback with no VFX assets, and real particles can be attached at any time without code changes.

**Procedural sprite placeholders:**
GestureFeedbackUI generates minimal circle, ring, arrow, and cone sprites programmatically when no sprite is assigned. This ensures every effect has a visible representation from the first run — no blank invisible UI elements blocking iteration.

**Alternatives considered:**
- Single UIFeedback script owning Canvas + animation — rejected, violates single responsibility and makes Canvas setup logic coupled to per-frame animation code.
- Manual Inspector wiring only — rejected, adds friction before PlayerController and Canvas exist; auto-populate removes that bottleneck entirely.
- Always require real sprites — rejected, forces art dependency before the input system is even verified working.

**Consequences:**
Both scripts attach to the same GameObject as InputManager. GestureFeedbackUI runs Awake first (same frame), builds the Canvas, and injects references into UIFeedback before UIFeedback's Awake event subscriptions fire. Auto-populate via reflection is safe at runtime and requires no UnityEditor namespace. Any of the procedural sprites can be replaced with real art by assigning a sprite in GestureFeedbackUI's Inspector — zero code changes.

---

## ADR-008: Tilt Input — Axis Mapping, Calibration Strategy, and Self-Correcting Baseline

**Date:** feature/ui-feedback
**Status:** Accepted

**Context:**
Tilt input via accelerometer went through three iterations before reaching correct behavior. Each iteration surfaced a real device behavior that wasn't apparent from documentation alone. This ADR records the decisions and discoveries so future contributors understand why the implementation looks the way it does.

**Decision:**
Implement a three-layer tilt system arrived at through on-device testing:

- **Layer 1 — Snap on first touch** (`CalibrateNeutral()`): Called on `TouchPhase.Began` of the first touch. The device is definitively in the player's hand at this moment. Auto-calibrate via `Invoke(0.5f)` was unreliable — the delay sometimes fired before the player picked up the phone, capturing a flat baseline instead of a natural hold angle.
- **Layer 2 — Continuous low-pass self-correction**: Every frame, `_tiltNeutral` lerps toward the current accelerometer reading at `tiltSmoothingRate`. This self-corrects baseline drift as the player shifts their hold angle during play — no manual recalibration needed. Rate 0.5 gives ~2s to re-center. Standard approach for casual mobile games where players move freely.
- **Layer 3 — Deadzone**: Magnitude below `tiltDeadzone` treated as zero. Suppresses resting jitter from the accelerometer when the device is held still.

**Android portrait Y-axis inversion:**
Left/right tilt (X axis) mapped correctly with no adjustment. Forward/back tilt (Y axis) was inverted — tilting the top of the phone away moved the arrow downward instead of upward. Root cause: on Android in portrait orientation, accelerometer Y increases when the bottom of the phone tilts away from the player (opposite of screen-space up convention). Fixed by negating `delta.y` when constructing `rawTilt`:

```csharp
Vector2 rawTilt = new Vector2(delta.x, -delta.y) * tiltSensitivity;
```

**Hold/pinch hysteresis (companion fix):**
Two-finger hold was immediately hijacked by pinch when fingers micro-moved while settling onto the screen. Fixed with a hysteresis lock in `HandleTwoTouches()`: fingers must remain stationary for `HoldLockDelay` (0.15s) before hold locks in, and once locked require `HoldBreakThreshold` (12px) movement to break back to pinch. The deliberate asymmetry between entry and break thresholds is the key design decision — mirrors standard gesture disambiguators in iOS/Android native recognizers.

**Alternatives considered:**
- Gyroscope delta instead of accelerometer absolute — more precise but higher battery cost and more complex normalization across devices; accelerometer sufficient for casual drift gameplay.
- Single calibration snapshot only — rejected, doesn't account for hold angle drift during play.
- Invoke(delay) auto-calibrate — rejected after on-device testing showed unreliable capture timing.
- Explicit recalibrate button — viable, kept as a future option; first-touch + low-pass filter makes it unnecessary for most sessions.

**Consequences:**
`HandleTilt()` is self-contained — all three layers run inside one method. `CalibrateNeutral()` remains public so a UI recalibrate button can call it explicitly if needed. The Y-axis negation and its rationale are documented inline so future contributors don't rediscover it. See inline comments in `HandleTilt()` for the full axis mapping reference.

---

---

## ADR-009: Rigidbody Force-Based Movement over Transform

**Date:** feature/netcode-foundation (planned)
**Status:** Accepted — implemented and verified on Galaxy A17 5G


**Context:**
PlayerController needs a movement model that feels satisfying on mobile.
Two approaches exist in Unity: direct transform manipulation (move X units
per frame) and Rigidbody physics (apply forces, let physics resolve position).
The game's name is Drift — the movement feel is central to the identity.

**Decision:**
Rigidbody force-based movement. Swipe gesture applies an impulse force in
the swipe direction. Tilt applies a continuous ambient force each frame.
Drag (GameConstants.PlayerLinearDrag) bleeds momentum on release.
Speed is capped at GameConstants.PlayerMaxSpeed.

**Alternatives considered:**
- Transform-based movement — snappy but no inertia. Doesn't match the
  "drift" fantasy. Also harder to integrate with physics-based world objects.
- Character Controller — designed for ground-based movement with gravity.
  Overkill and wrong abstraction for a top-down floaty collector.

**Consequences:**
Movement has natural momentum and feels weighted without being slow.
Rigidbody requires careful drag tuning — values live in GameConstants so
balance changes need no code edits. Physics interactions with world objects
(orbs, stations) work for free. NetworkTransform sync works cleanly with
Rigidbody position.

---

## ADR-010: Multiplayer Services 2.x over Individual UGS SDKs

**Date:** feature/netcode-foundation
**Status:** Accepted

**Context:**
The original architecture planned separate packages for Relay
(com.unity.services.relay) and Lobby (com.unity.services.lobby).
During implementation, Unity's Multiplayer Services 2.x package was
discovered — a unified SDK that replaces both individual packages and
adds an ISession abstraction layer.

**Decision:**
Adopt Multiplayer Services 2.x (com.unity.services.multiplayer 2.1.3).
Remove the separate Relay and Lobby package references from manifest.json.
Use MultiplayerService.Instance.CreateSessionAsync(options.WithRelayNetwork())
for host flow and JoinSessionByCodeAsync / JoinSessionByIdAsync for clients.

**Alternatives considered:**
- Keep individual Relay + Lobby SDKs — familiar from Euchre project but
  requires manual Relay allocation, UnityTransport configuration, and
  separate Lobby API calls. More code, more failure points.
- Custom relay server — rejected, no self-hosting for a casual mobile game.

**Consequences:**
Session creation, Relay allocation, join code generation, and Netcode
host/client startup all happen in a single SDK call. The SDK handles
NAT traversal internally. The old Unity.Services.Relay namespace no longer
exists — code written against it will not compile. Session browsing
(open rooms dashboard) is built in via QuerySessionsAsync.
ADR supersedes ADR-002's Relay implementation details.

---

## ADR-011: Three Camera Modes via CameraRig State Machine

**Date:** feature/netcode-foundation (planned)
**Status:** Accepted — implemented in feature/player-controller.
CycleMode() ready, wired to HUD button in feature/hud-and-polish.
Verified in editor, device test pending HUD button.


**Context:**
Different players have different spatial preferences for a top-down
collection game. Isometric gives the best awareness of surroundings.
Top-down is cleaner and more readable. Third-person is most immersive.
Rather than forcing one perspective, all three are offered.

**Decision:**
CameraRig.cs implements a state machine with three modes:
- CameraMode.Isometric — 45° angle, smooth follow. Default.
- CameraMode.TopDown — near-overhead, wider FOV.
- CameraMode.ThirdPerson — behind and above player, most immersive.
Mode toggled via HUD button. Preference persisted to PlayerPrefs using
GameConstants.CameraModePrefKey so it survives between sessions on the
same device. All modes use the same smooth follow speed
(GameConstants.CameraFollowSpeed).

**Alternatives considered:**
- Single fixed isometric camera — simpler but limits player preference.
- Let players set camera angle via drag gesture — conflicts with swipe
  movement input. Not viable given the input model.

**Consequences:**
CameraRig is self-contained. Adding a fourth mode later requires adding
an enum value and a case in the state machine — no other files change.
PlayerPrefs persistence is device-local — two players on the same session
can independently choose different camera modes.

---

## ADR-012: Session-Based Architecture, No Persistence This Build

**Date:** feature/netcode-foundation
**Status:** Accepted

**Context:**
The full game vision includes persistent worlds — a player's safe zones,
station progress, and inventory surviving between sessions (Firebase/Firestore).
The assignment deadline is ~3 days. Implementing persistence correctly
requires a backend data model, serialization, and conflict resolution
for concurrent players. That's a separate project.

**Decision:**
Session-based only for the assignment build. Each session starts fresh.
Host generates a new worldSeed, all state is in-memory via NetworkVariables,
everything clears on session end. Firebase persistence is documented as the
first post-submission feature in the DEVLOG roadmap.

The data model is designed to be persistence-ready: zoneStates[],
stationProgress[], inventory[], activeEffects[], and hiredNPCs[] are all
structured arrays that can be serialized to Firestore documents without
refactoring. The transition to persistence is additive, not breaking.

**Alternatives considered:**
- PlayerPrefs persistence (local only) — not true persistence, doesn't
  sync between players, misleading UX.
- Firebase now — correct long-term choice but not feasible in 3 days
  alongside the rest of the assignment scope.

**Consequences:**
No save/load code to write or debug. Sessions are stateless which
simplifies disconnect handling. Post-submission Firebase integration
slots directly into the existing NetworkVariable data model.

---

## ADR-013: Stub-First Entity Hierarchy with Interface Contracts

**Date:** feature/netcode-foundation
**Status:** Accepted

**Context:**
The game vision includes enemy orbs, ally NPCs, boss orbs, and hazard
zones — but none of these have behavior to implement yet. The question
is whether to define their class structure now or defer entirely.

**Decision:**
Define the interface contracts and enum values now, stub the classes.
IEntity (EntityType, Faction), IDamageable (TakeDamage, OnDeath), and
IInteractable (OnInteract, GetInteractLabel) are fully defined.
EntityType and Faction enums include all planned values.
EnemyOrb.cs and AllyNPC.cs exist as class files that implement the
interfaces but contain no behavior.

**Rationale:**
An interface is a promise. Defining IEntity now means PlayerController,
EnemyOrb, and AllyNPC all speak the same language — WorldManager's spawn
system, the targeting system, and the EntityStatTable all work against
the interface, not the concrete type. Adding behavior to a stub later
requires filling in methods, not changing callers.

**Consequences:**
Zero extra runtime cost — empty classes are compiled away. The
EntityStatTable ScriptableObject can be created and populated in
feature/world-layer without waiting for AI behavior. Every future entity
type has a clearly marked entry point.

---

## ADR-014: ZoneState Enum as Single Source of Truth

**Date:** feature/netcode-foundation (planned)
**Status:** Accepted — implementation in feature/world-layer

**Context:**
The world has zones with complex interdependencies: spawn rates depend
on zone safety, hazards depend on zone state, shop availability depends
on zone state, fog of war depends on discovery state. Multiple boolean
flags per zone would create inconsistent combinations and scattered
conditional logic.

**Decision:**
A single ZoneState enum per zone, stored as NetworkVariable<ZoneState[]>
on WorldManager. States: Undiscovered, Discovered, Contested, Safe.
(FUTURE: Corrupted.) Every system reads this one value:
- WorldManager.SpawnTick() looks up SpawnRateTable[zoneState]
- DomainZone renders fog/reveal/hazard visuals based on zoneState
- ZoneShop.OnInteract() checks zoneState == Safe before opening
- EnemyOrb despawns when its zone transitions to Safe

One NetworkVariable write cascades to all dependent behavior automatically.
No additional sync messages needed.

**Alternatives considered:**
- Separate bool flags (isDiscovered, isSafe, isHazardous) — allows
  impossible combinations (safe AND hazardous). Requires multiple
  NetworkVariables. Logic scattered across systems.
- Zone objects with full NetworkBehaviour state — overcomplicated,
  expensive for an 8x8 zone grid.

**Consequences:**
Adding a new zone state (e.g. Corrupted) requires one enum value and
one case in each reading system. The SpawnRateTable ScriptableObject
has one row per ZoneState — designers can tune rates in the Inspector
without touching code.

---

## ADR-015: WorldSeed Pattern for Deterministic Layout

**Date:** feature/netcode-foundation (planned)
**Status:** Accepted — implementation in feature/world-layer

**Context:**
The world layout (zone positions, hazard placements, station locations)
needs to be identical on all clients without sending the full layout
over the network. Two options: server sends layout data to all clients,
or all clients generate the same layout from a shared seed.

**Decision:**
Host generates a random int seed at session start, stores it in
NetworkVariable<int> worldSeed on WorldManager. Each client reads this
seed and calls UnityEngine.Random.InitState(worldSeed) before running
the layout generation algorithm. Because the algorithm is deterministic
and all clients use the same seed, all clients produce the identical
world layout with zero extra network cost.

**Alternatives considered:**
- Server sends full zone layout array — proportional to world size,
  adds join latency, requires serialization of layout data.
- Each client generates independently — would produce different layouts,
  breaking cooperative play (different players see different worlds).
- Procedural generation per-frame server-side only — clients would need
  constant sync of every orb and zone state update.

**Consequences:**
World layout sync cost is exactly 4 bytes (one int). All procedural
generation runs client-side from the seed. Layout algorithm must be
purely deterministic — no Time.time, no platform-specific random,
no floating point operations that might diverge across devices.
The seed is set once at session start and never changes.