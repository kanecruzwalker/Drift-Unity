/// <summary>
/// GameConstants — single source of truth for all magic numbers in Drift.
///
/// No other script should hardcode tuning values. Reference these constants
/// instead so balance changes and world scaling require edits in one place only.
///
/// Sections:
///   World         — bounds, zone grid, soft boundary
///   Spawn         — orb and enemy rates per ZoneState (see also SpawnRateTable SO)
///   Station       — deposit thresholds and progress caps
///   Player        — movement forces, inventory, effect and NPC caps
///   Camera        — follow speeds and angle presets per CameraMode
///   Networking    — tick rates, timeouts
///   UI            — HUD layout constants
/// </summary>
public static class GameConstants
{
    // ─────────────────────────────────────────────────────────────────────────
    // WORLD
    // ─────────────────────────────────────────────────────────────────────────

    /// Half-width and half-height of the total playable world in Unity units.
    /// Full world = WorldHalfExtent * 2 on each axis (~8x a 1080p screen at 1u/px).
    public const float WorldHalfExtent = 80f;

    /// Number of zones along each axis of the zone grid (total = ZoneGridSize²).
    public const int ZoneGridSize = 8;

    /// Force magnitude applied to push the player back when they reach the world edge.
    public const float WorldBoundaryPushbackForce = 12f;

    /// Distance from the world edge at which the soft boundary pushback begins.
    public const float WorldBoundaryPushbackStartDistance = 5f;

    // ─────────────────────────────────────────────────────────────────────────
    // SPAWN
    // ─────────────────────────────────────────────────────────────────────────

    /// Seconds between WorldManager spawn ticks (server only).
    public const float SpawnTickInterval = 3f;

    /// Maximum orbs allowed in a single zone at one time (prevents overpopulation).
    public const int MaxOrbsPerZone = 8;

    /// Maximum enemies allowed in a single zone at one time.
    public const int MaxEnemiesPerZone = 4;

    /// Waypoint patrol radius for Patrol orbs around their spawn point.
    public const float PatrolOrbWaypointRadius = 6f;

    /// Speed at which Guide orbs drift toward undiscovered zone edges.
    public const float GuideOrbDriftSpeed = 1.5f;

    /// Distance from WorldHalfExtent at which Guide orbs are despawned.
    /// Keeps guides from clipping the world edge visually.
    public const float GuideOrbDespawnMargin = 5f;

    // ─────────────────────────────────────────────────────────────────────────
    // STATION
    // ─────────────────────────────────────────────────────────────────────────

    /// Resources required to fill a station and trigger ZoneState → Safe transition.
    public const int StationFillThreshold = 20;

    /// Resources required to trigger ZoneState → Contested (partial fill indicator).
    public const int StationContestedThreshold = 15;

    /// Radius around a station within which a player can deposit resources.
    public const float StationDepositRadius = 3f;

    /// Resources deposited per second when a player is within StationDepositRadius.
    /// Drain is one unit per tick at this rate — strategic, not instant (ADR-016).
    public const float DepositRatePerSecond = 2f;

    /// How often (seconds) the deposit tick fires while a player is in range.
    /// 1f / DepositRatePerSecond = 0.5s between ticks for 2 units/sec.
    public const float DepositTickInterval = 0.5f;

    // ─────────────────────────────────────────────────────────────────────────
    // PLAYER
    // ─────────────────────────────────────────────────────────────────────────

    /// Force applied to the Rigidbody per swipe pixel delta.
    public const float SwipeForceMultiplier = 0.08f;

    /// Ambient tilt force applied to Rigidbody per frame from accelerometer lean.
    public const float TiltDriftForce = 2.5f;

    /// Rigidbody drag applied at rest — controls how quickly momentum bleeds off.
    public const float PlayerLinearDrag = 1.8f;

    /// Maximum Rigidbody velocity magnitude — caps top speed in normal movement.
    public const float PlayerMaxSpeed = 12f;

    /// Impulse force applied on double-tap dash burst.
    public const float DashBurstForce = 18f;

    /// Starting health for all players.
    public const float PlayerMaxHealth = 100f;

    /// Number of inventory slots per player.
    public const int InventorySlotCount = 6;

    /// Maximum simultaneous active buff/debuff effects per player.
    public const int MaxActiveEffects = 4;

    /// Maximum simultaneously hired NPCs per player.
    public const int MaxHiredNPCs = 3;

    /// Radius of the single-tap precision collect hitbox in world units.
    public const float TapCollectRadius = 1.2f;

    /// Radius of the two-finger hold-pulse area collect at full charge.
    public const float HoldPulseCollectRadius = 6f;

    /// Radius multiplier for the three-finger burst collect (applied to HoldPulseCollectRadius).
    public const float ThreeFingerBurstRadiusMultiplier = 3f;

    /// Maximum resource transfer per 4-finger share gesture activation.
    public const int ShareGestureTransferAmount = 10;

    // ─────────────────────────────────────────────────────────────────────────
    // CAMERA
    // ─────────────────────────────────────────────────────────────────────────

    /// Smooth follow speed for all camera modes (higher = snappier).
    public const float CameraFollowSpeed = 4f;

    /// Isometric camera angle in degrees (applied as X rotation on the camera rig).
    public const float IsometricCameraAngle = 45f;

    /// Top-down camera angle in degrees.
    public const float TopDownCameraAngle = 70f;

    /// Third-person camera follow distance behind the player.
    public const float ThirdPersonFollowDistance = 8f;

    /// Third-person camera height above the player.
    public const float ThirdPersonCameraHeight = 4f;

    /// PlayerPrefs key for persisting camera mode choice across sessions.
    public const string CameraModePrefKey = "Drift_CameraMode";

    // ─────────────────────────────────────────────────────────────────────────
    // NETWORKING
    // ─────────────────────────────────────────────────────────────────────────

    /// How often (seconds) the server broadcasts world state to all clients.
    public const float NetworkWorldSyncInterval = 0.1f;

    /// Seconds before an idle lobby session is cleaned up by LobbyManager.
    public const float LobbyIdleTimeoutSeconds = 300f;

    /// Maximum players per session (Relay free tier supports up to 10 connections).
    public const int MaxPlayersPerSession = 8;

    /// Length of the join code string generated by RelayManager.
    public const int JoinCodeLength = 6;

    // ─────────────────────────────────────────────────────────────────────────
    // UI / HUD
    // ─────────────────────────────────────────────────────────────────────────

    /// Size of the minimap overlay in screen pixels (square).
    public const float MinimapSizePixels = 180f;

    /// World-to-minimap scale factor (how many world units map to one minimap pixel).
    public const float MinimapWorldScale = 0.8f;

    /// Seconds a HUD notification stays visible before fading.
    public const float HUDNotificationDuration = 2.5f;

    /// Seconds for the join code display to auto-hide after session start.
    public const float JoinCodeAutoHideDelay = 10f;



}