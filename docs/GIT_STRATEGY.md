# Git Strategy — Drift

## Philosophy

This repo is employer-visible. The commit history, PR descriptions, and branch structure should tell the story of how the project was built — not just what was built. An interviewer should be able to open the network graph and understand the development arc in 30 seconds.

---

## Branch Structure

```
main        — stable, always-deployable. Tagged at milestones.
develop     — integration branch. Feature branches PR here first.

docs/architecture             — all docs/ content, README, secrets-setup, initial commit
feature/input-system          — InputManager scaffold, tap + swipe, GestureEvents, Unity Remote verified
feature/tilt-input            — accelerometer ambient drift + aimed collect cone
feature/multi-finger-gestures — 3/4/5 finger classification and action dispatch
feature/netcode-foundation    — NetworkManager, RelayManager, LobbyManager
feature/relay-lobby           — join code UI flow, end-to-end relay connect
feature/resource-orbs         — WorldManager spawn, ResourceOrb collect RPC
feature/deposit-stations      — DepositStation NetworkVariable, domain unlock
feature/ui-feedback           — UIFeedback.cs, all visual feedback FX
```

**Rules:**
- `main` never receives direct commits — only milestone merges from `develop`
- Feature branches cut from `develop`, PR back to `develop`
- Each PR includes a description: what was built, why it was structured this way, what it unlocks next

---

## Commit Message Convention

Format: `type(scope): short imperative description`

**Types:** `feat` / `fix` / `refactor` / `docs` / `test` / `chore`
**Scopes:** `input` / `netcode` / `world` / `ui` / `player` / `core` / `docs`

### Full Commit Log Reference

```
feat(input): scaffold InputManager and GestureEvents event contract
feat(input): single-tap precision collect with tap disambiguation timer
feat(input): double-tap dash burst event
feat(input): stub OnTripleTap — reserved for future interactable interaction
feat(input): swipe movement with OnSwipe event dispatch
feat(input): two-finger pinch detection and OnPinchDelta event
feat(input): hold duration tracking with OnHoldProgress and OnHoldComplete
feat(input): 3-finger hold gesture with OnThreeFingerHold dispatch
feat(input): 4-finger share gesture and 5-finger splay domain expand
feat(input): accelerometer tilt drift current with OnTiltChanged event
feat(input): aimed collect cone — tilt offsets hold-pulse radius direction
feat(netcode): initialize NetworkManager with Unity Relay transport
feat(netcode): RelayManager host allocation and join code generation
feat(netcode): LobbyManager client join-by-code flow
feat(world): WorldManager server-side orb spawn timer
feat(world): ResourceOrb ServerRpc collect request and despawn
feat(world): DepositStation NetworkVariable progress and unlock trigger
feat(ui): tap collect pop and double-tap speed trail flare
feat(ui): radial fill feedback for hold duration gesture
feat(ui): 3-ring expand animation for three-finger burst collect
feat(ui): beam effect for four-finger resource share
feat(ui): domain pulse animation for five-finger splay
feat(ui): tilt direction arrow for ambient drift feedback
feat(ui): asymmetric collect cone visual for aimed tilt
fix(input): resolve gesture conflict between pinch and 3-finger hold
fix(input): apply deadzone to tilt to suppress resting-device jitter
fix(input): disambiguate single-tap from swipe initiation via movement threshold
fix(netcode): handle host disconnect gracefully
docs: add ADR-001 through ADR-006 to DECISIONS.md
chore: configure Android build settings and keystore
chore: attach APK to GitHub Release v1.0.0
feat(ui): UIFeedback and GestureFeedbackUI — player-facing visual feedback for all 11 inputs
feat(ui): FeedbackService audio/haptic stub wired to all input event handlers
fix(input): hold/pinch hysteresis guard — HoldLockDelay and HoldBreakThreshold
fix(input): tilt first-touch calibration replacing unreliable Invoke timer
fix(input): continuous low-pass tilt baseline for self-correcting drift during play
fix(input): negate tilt Y-axis for correct Android portrait forward/back mapping
docs: add ADR-007 UIFeedback architecture and ADR-008 tilt calibration discoveries
```

---

## Milestone Tags

```
v0.1.0 — all 11 inputs firing events, single player, Unity Remote verified on iPhone
v0.2.0 — multiplayer connected, two players visible in shared world via Relay
v0.3.0 — full game loop: collect (tap + pulse), deposit, domain expand
v1.0.0 — APK deployed to Android, playtested, submission-ready
```

---

## GitHub Release (v1.0.0)

Attach to the release:
- `Drift-v1.0.0.apk` — directly installable on Android
- Release notes summarizing the 11-input system and multiplayer architecture
- Link to demo recording or GIF

Anyone viewing the repo can download and play without a build environment.

---

## README Structure (final)

```markdown
# Drift — Cooperative Mobile Resource World

[Demo GIF]

## What It Is
## Input Reference (tap table + multi-finger table + tilt table)
## Multiplayer — How to Play Together
## Architecture Highlights
## Build Instructions
## Assignment Context
```
