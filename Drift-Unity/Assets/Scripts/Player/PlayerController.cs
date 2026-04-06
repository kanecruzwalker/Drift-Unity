using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// PlayerController — the local player's networked movement and ability controller.
///
/// Implements:
///   IDamageable — receives damage from hazards and enemies (PvE mode)
///   IEntity     — provides EntityType and Faction for world system lookups
///
/// Movement model (ADR-009):
///   Rigidbody force-based. Swipe applies impulse. Tilt adds ambient drift.
///   Drag bleeds momentum. Speed capped at GameConstants.PlayerMaxSpeed.
///   All tuning values live in GameConstants — no magic numbers here.
///
/// Input:
///   Subscribes to GestureEvents on OnEnable, unsubscribes on OnDisable.
///   InputManager fires all events — PlayerController never reads hardware directly.
///
/// Networking:
///   Extends NetworkBehaviour. Owner drives movement locally, NetworkTransform
///   syncs position to all clients. Non-owners are kinematic (no force applied).
///
/// Tag: Must be tagged "Player" in the Inspector.
///   UIFeedback uses FindWithTag("Player") for the beam/arrow anchor.
///   GameManager uses FindWithTag("Player") for session setup.
///
/// Attach to: the Player prefab root alongside NetworkObject and NetworkTransform.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour, IDamageable, IEntity
{
    // ─────────────────────────────────────────────────────────────────────────
    // IENTITY
    // ─────────────────────────────────────────────────────────────────────────

    /// This is a player entity — drives EntityStatTable lookup and targeting.
    public EntityType EntityType => EntityType.Player;

    /// Players are in the Player faction — friendly to Ally, hostile to Hostile.
    public Faction Faction => Faction.Player;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR — References
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("NetworkPlayer component on this GameObject — provides health and resource NetworkVariables.")]
    [SerializeField] private NetworkPlayer networkPlayer;

    [Tooltip("PlayerVisuals component on this GameObject — driven by movement state.")]
    [SerializeField] private PlayerVisuals playerVisuals;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    private Rigidbody _rb;

    /// Current tilt vector from InputManager — applied as ambient force each frame.
    private Vector2 _currentTilt = Vector2.zero;

    /// True while a dash burst cooldown is active — prevents double-tap spam.
    private bool _dashOnCooldown;

    /// Cooldown duration for dash burst in seconds.
    private const float DashCooldown = 0.8f;

    /// True if this player is alive (health > 0).
    public bool IsAlive { get; private set; } = true;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        // Configure Rigidbody for top-down 2.5D movement.
        // Freeze Y position and X/Z rotation — player slides on the XZ plane.
        _rb.constraints = RigidbodyConstraints.FreezePositionY |
                          RigidbodyConstraints.FreezeRotationX |
                          RigidbodyConstraints.FreezeRotationZ;
        _rb.linearDamping = GameConstants.PlayerLinearDrag;
        _rb.angularDamping = 5f;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Auto-resolve references if not set in Inspector.
        if (networkPlayer == null)
            networkPlayer = GetComponent<NetworkPlayer>();
        if (playerVisuals == null)
            playerVisuals = GetComponent<PlayerVisuals>();
    }

    public override void OnNetworkSpawn()
    {
        // Non-owners are kinematic — NetworkTransform drives their position.
        // Only the owning client applies forces.
        if (!IsOwner)
        {
            _rb.isKinematic = true;
            return;
        }

        Debug.Log($"[PlayerController] Spawned as owner. ClientId={OwnerClientId}");
    }

    private void OnEnable() => SubscribeToEvents();
    private void OnDisable() => UnsubscribeFromEvents();

    private void FixedUpdate()
    {
        if (!IsOwner || !IsAlive) return;
        ApplyTiltDrift();
        ClampSpeed();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INPUT EVENT SUBSCRIPTIONS
    // ─────────────────────────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        GestureEvents.OnSwipe += HandleSwipe;
        GestureEvents.OnDoubleTap += HandleDoubleTap;
        GestureEvents.OnSingleTap += HandleSingleTap;
        GestureEvents.OnTiltChanged += HandleTiltChanged;
        GestureEvents.OnFourFingerHold += HandleFourFingerHold;
    }

    private void UnsubscribeFromEvents()
    {
        GestureEvents.OnSwipe -= HandleSwipe;
        GestureEvents.OnDoubleTap -= HandleDoubleTap;
        GestureEvents.OnSingleTap -= HandleSingleTap;
        GestureEvents.OnTiltChanged -= HandleTiltChanged;
        GestureEvents.OnFourFingerHold -= HandleFourFingerHold;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MOVEMENT — Swipe
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts screen-space swipe delta to world-space force and applies it
    /// to the Rigidbody. Screen X maps to world X, screen Y maps to world Z
    /// (top-down XZ plane movement).
    ///
    /// Force is scaled by SwipeForceMultiplier — tune in GameConstants.
    /// Only applied by the owning client.
    /// </summary>
    private void HandleSwipe(Vector2 delta)
    {
        if (!IsOwner || !IsAlive) return;

        // Transform screen-space swipe delta into camera-relative world space.
        // Raw delta.x/delta.y are screen axes — we project them onto the camera's
        // forward and right vectors (flattened to XZ plane) so movement matches
        // what the player sees regardless of camera angle or mode.
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;

        // Flatten to XZ plane — remove any Y component so player stays grounded.
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 force = (camRight * delta.x + camForward * delta.y)
                        * GameConstants.SwipeForceMultiplier;
        _rb.AddForce(force, ForceMode.Impulse);

        playerVisuals?.SetMoving(delta.magnitude > 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MOVEMENT — Tilt (ambient drift)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Caches the latest tilt vector each frame. Applied as a continuous
    /// ambient force in FixedUpdate via ApplyTiltDrift().
    /// Tilt is always active — creates the passive drift current feel.
    /// </summary>
    private void HandleTiltChanged(Vector2 tilt)
    {
        if (!IsOwner) return;
        _currentTilt = tilt;
    }

    /// <summary>
    /// Applies the cached tilt vector as a continuous ambient force.
    /// Called in FixedUpdate so force application is physics-frame aligned.
    /// Force magnitude is lower than swipe — tilt drifts, swipe drives.
    /// </summary>
    private void ApplyTiltDrift()
    {
        if (_currentTilt.magnitude < 0.01f) return;

        Vector3 tiltForce = new Vector3(_currentTilt.x, 0f, _currentTilt.y)
                            * GameConstants.TiltDriftForce;
        _rb.AddForce(tiltForce, ForceMode.Force);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ABILITIES — Double tap (dash burst)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a large impulse in the current velocity direction.
    /// If the player is stationary, dash fires in the last swipe direction.
    /// Gated by DashCooldown to prevent spamming.
    /// </summary>
    private void HandleDoubleTap()
    {
        if (!IsOwner || !IsAlive || _dashOnCooldown) return;

        // Dash in current movement direction.
        // If nearly stationary, use the last known swipe direction instead.
        // Never dash in an arbitrary world direction — no-op if no direction available.
        Vector3 flatVel = new Vector3(
            _rb.linearVelocity.x, 0f, _rb.linearVelocity.z);

        if (flatVel.magnitude < 0.1f)
        {
            // No current velocity — skip dash rather than firing in random direction.
            // Player must be moving to dash. This makes dash feel intentional.
            return;
        }

        Vector3 dashDir = flatVel.normalized;
        _rb.AddForce(dashDir * GameConstants.DashBurstForce, ForceMode.Impulse);
        _dashOnCooldown = true;
        Invoke(nameof(ResetDashCooldown), DashCooldown);

        playerVisuals?.TriggerDash();
        Debug.Log($"[PlayerController] Dash burst. Dir={dashDir}");
    }

    private void ResetDashCooldown() => _dashOnCooldown = false;

    // ─────────────────────────────────────────────────────────────────────────
    // ABILITIES — Single tap (precision collect)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to collect a ResourceOrb at the tap screen position.
    /// Converts screen position to world ray, checks for orbs within
    /// GameConstants.TapCollectRadius, and sends a collect ServerRpc
    /// to the nearest valid orb.
    ///
    /// Collection is server-authoritative — client sends request,
    /// server validates and despawns (ADR-004).
    /// </summary>
    private void HandleSingleTap(Vector2 screenPos)
    {
        if (!IsOwner || !IsAlive) return;

        Ray ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 100f))
        {
            ResourceOrb orb = hit.collider.GetComponent<ResourceOrb>();
            if (orb != null)
            {
                float dist = Vector3.Distance(transform.position, hit.point);
                if (dist <= GameConstants.TapCollectRadius * 3f)
                {
                    orb.RequestCollectServerRpc();
                    Debug.Log($"[PlayerController] Tap collect requested. Orb={orb.name}");
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ABILITIES — Four finger (resource share)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transfers GameConstants.ShareGestureTransferAmount resources to the
    /// nearest partner player. Fires a ServerRpc on NetworkPlayer which
    /// validates both players exist and the caller has sufficient resources.
    ///
    /// Also provides the partner world position to UIFeedback for the
    /// beam direction — this is what wires the 4-finger beam visual (ADR-007).
    /// </summary>
    private void HandleFourFingerHold()
    {
        if (!IsOwner || !IsAlive) return;

        NetworkPlayer nearestPartner = FindNearestPartner();
        if (nearestPartner == null) {
            //No partner -clear beam so UIFeedback shows searching ring.
            UIFeedback.Instance?.ClearBeamTarget();
            return;
        }

        // Wire the beam direction to UIFeedback.
        // UIFeedback reads _partnerScreenPos each frame in UpdateFourFingerFeedback.
        UpdateBeamTarget(nearestPartner);

        networkPlayer?.ShareResourcesServerRpc(
            nearestPartner.NetworkObjectId,
            GameConstants.ShareGestureTransferAmount);

        Debug.Log($"[PlayerController] Sharing {GameConstants.ShareGestureTransferAmount} " +
                  $"resources with ClientId={nearestPartner.OwnerClientId}");
    }

    /// <summary>
    /// Finds the nearest NetworkPlayer that is not this player.
    /// Returns null if no other players are in the session.
    /// </summary>
    private NetworkPlayer FindNearestPartner()
    {
        NetworkPlayer nearest = null;
        float minDist = float.MaxValue;

        foreach (var player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.OwnerClientId == OwnerClientId) continue;

            float dist = Vector3.Distance(
                transform.position,
                player.worldPosition.Value);

            if (dist < minDist)
            {
                minDist = dist;
                nearest = player;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Updates UIFeedback's beam target screen position using the partner's
    /// world position from their NetworkVariable. This wires the four-finger
    /// beam visual that was placeholder in feature/ui-feedback (see ADR-007).
    /// </summary>
    private void UpdateBeamTarget(NetworkPlayer partner)
    {
        if (Camera.main == null) return;

        // Convert partner world position to screen space and pass to UIFeedback.
        // This wires the four-finger beam visual that was placeholder in feature/ui-feedback.
        // See ADR-007 — this is the explicit TODO from that branch.
        Vector3 partnerScreenPos = Camera.main.WorldToScreenPoint(
            partner.worldPosition.Value);

        UIFeedback.Instance?.SetBeamTarget(partnerScreenPos);

        Debug.Log($"[PlayerController] Beam target: {partner.worldPosition.Value}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PHYSICS HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clamps Rigidbody velocity to GameConstants.PlayerMaxSpeed each physics
    /// frame. Preserves direction — only reduces magnitude when over the cap.
    /// </summary>
    private void ClampSpeed()
    {
        Vector3 flatVel = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        if (flatVel.magnitude > GameConstants.PlayerMaxSpeed)
        {
            Vector3 clamped = flatVel.normalized * GameConstants.PlayerMaxSpeed;
            _rb.linearVelocity = new Vector3(clamped.x, _rb.linearVelocity.y, clamped.z);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WORLD BOUNDARY
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies soft pushback force when the player approaches the world edge.
    /// Called in FixedUpdate implicitly via the boundary check below.
    /// Soft boundary — player feels resistance before being stopped,
    /// not a hard wall (ADR-009, GameConstants.WorldBoundaryPushbackForce).
    /// </summary>
    private void Update()
    {
        if (!IsOwner) return;
        ApplyWorldBoundary();
    }

    private void ApplyWorldBoundary()
    {
        Vector3 pos = transform.position;
        float extent = GameConstants.WorldHalfExtent;
        float start = extent - GameConstants.WorldBoundaryPushbackStartDistance;
        Vector3 push = Vector3.zero;

        if (pos.x > start) push.x -= (pos.x - start) * GameConstants.WorldBoundaryPushbackForce;
        if (pos.x < -start) push.x += (-start - pos.x) * GameConstants.WorldBoundaryPushbackForce;
        if (pos.z > start) push.z -= (pos.z - start) * GameConstants.WorldBoundaryPushbackForce;
        if (pos.z < -start) push.z += (-start - pos.z) * GameConstants.WorldBoundaryPushbackForce;

        if (push != Vector3.zero)
            _rb.AddForce(push, ForceMode.Force);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDAMAGEABLE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Receives damage from hazards and enemies. Delegates to NetworkPlayer
    /// which applies damage server-side via its NetworkVariable health.
    /// Only callable server-side — clients send a ServerRpc to request damage.
    /// </summary>
    public void TakeDamage(float amount, ulong sourceId)
    {
        if (!IsServer || !IsAlive) return;
        networkPlayer?.ApplyDamage(amount, sourceId);
        Debug.Log($"[PlayerController] TakeDamage. Amount={amount} Source={sourceId}");
    }

    /// <summary>
    /// Called by NetworkPlayer when health reaches zero.
    /// Disables movement input and triggers death visuals.
    /// FUTURE: trigger respawn timer, drop resources.
    /// </summary>
    public void OnDeath()
    {
        IsAlive = false;
        playerVisuals?.TriggerDeath();
        Debug.Log($"[PlayerController] Player died. ClientId={OwnerClientId}");
        // FUTURE: StartCoroutine(RespawnAfterDelay(GameConstants.RespawnDelay));
    }
}