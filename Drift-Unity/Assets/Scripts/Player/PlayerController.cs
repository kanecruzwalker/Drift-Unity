using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using System.Collections;

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

    /// Coroutine handle for the active deposit drain tick — null when not depositing.
    private Coroutine _depositCoroutine;

    /// The station currently being deposited into — null when out of range.
    private DepositStation _nearbyStation;

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
        GestureEvents.OnHoldComplete += HandleHoldComplete;
        GestureEvents.OnThreeFingerHold += HandleThreeFingerBurst;
    }

    private void UnsubscribeFromEvents()
    {
        GestureEvents.OnSwipe -= HandleSwipe;
        GestureEvents.OnDoubleTap -= HandleDoubleTap;
        GestureEvents.OnSingleTap -= HandleSingleTap;
        GestureEvents.OnTiltChanged -= HandleTiltChanged;
        GestureEvents.OnFourFingerHold -= HandleFourFingerHold;
        GestureEvents.OnHoldComplete -= HandleHoldComplete;
        GestureEvents.OnThreeFingerHold -= HandleThreeFingerBurst;
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
    /// Attempts to collect a ResourceOrb near the tap position.
    /// Uses an overlap sphere at the player's position rather than a screen
    /// raycast — more reliable against bobbing orbs and avoids perspective
    /// misalignment on isometric camera angles.
    ///
    /// Finds all orbs within TapCollectRadius, picks the closest one,
    /// and sends a collect ServerRpc. Server validates and despawns (ADR-004).
    /// </summary>
    private void HandleSingleTap(Vector2 screenPos)
    {
        if (!IsOwner || !IsAlive) return;

        // Use overlap sphere at player position — consistent hit detection
        // regardless of orb Y-bobbing or camera angle.
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            GameConstants.TapCollectRadius * 3f);

        ResourceOrb closest = null;
        float minDist = float.MaxValue;

        foreach (var hit in hits)
        {
            ResourceOrb orb = hit.GetComponent<ResourceOrb>();
            if (orb == null) continue;
            if (orb.orbType.Value == OrbType.Hazard) continue;

            float dist = Vector3.Distance(transform.position, hit.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = orb;
            }
        }

        if (closest != null)
        {
            closest.RequestCollectServerRpc();
            Debug.Log($"[PlayerController] Tap collect requested. " +
                      $"Orb={closest.name} dist={minDist:F1}");
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

    // ─────────────────────────────────────────────────────────────────────────
    // ABILITIES — Hold complete (area vacuum collect)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired once when the two-finger hold charge reaches full.
    /// Sends a single area collect RPC to the server with HoldPulseCollectRadius.
    /// Server finds all eligible orbs within radius and awards resources.
    /// </summary>
    private void HandleHoldComplete()
    {
        if (!IsOwner || !IsAlive) return;

        WorldManager.Instance?.RequestAreaCollectServerRpc(
            GameConstants.HoldPulseCollectRadius);

        Debug.Log($"[PlayerController] Hold-pulse area collect. " +
                  $"Radius={GameConstants.HoldPulseCollectRadius}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ABILITIES — Three finger burst (3x radius area collect)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired each frame while 3 fingers are held.
    /// Uses the same area collect RPC as hold-pulse but with 3x radius.
    /// Rate-limited to once per second to avoid spamming the server.
    /// </summary>
    private float _burstCooldown = 0f;

    private void HandleThreeFingerBurst()
    {
        if (!IsOwner || !IsAlive) return;
        if (Time.time < _burstCooldown) return;

        float burstRadius = GameConstants.HoldPulseCollectRadius *
                            GameConstants.ThreeFingerBurstRadiusMultiplier;

        WorldManager.Instance?.RequestAreaCollectServerRpc(burstRadius);
        _burstCooldown = Time.time + 1f;

        Debug.Log($"[PlayerController] Three-finger burst collect. " +
                  $"Radius={burstRadius}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PROXIMITY DEPOSIT
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called from Update to check proximity to all deposit stations.
    /// Starts the deposit drain coroutine when entering range,
    /// stops it when leaving range. Auto-engages and auto-disengages.
    /// </summary>
    private void CheckDepositProximity()
    {
        if (!IsOwner || !IsAlive) return;

        DepositStation closest = FindClosestStation();

        if (closest != null)
        {
            // Entered range of a new station or same station.
            if (_nearbyStation != closest)
            {
                // Switched stations or just entered range — restart coroutine.
                StopDepositCoroutine();
                _nearbyStation = closest;
                _depositCoroutine = StartCoroutine(DepositDrainCoroutine());
                Debug.Log($"[PlayerController] Entered deposit range of station " +
                          $"in zone {closest.ZoneIndex.Value}");
            }
        }
        else
        {
            // Out of range of all stations.
            if (_nearbyStation != null)
            {
                StopDepositCoroutine();
                Debug.Log("[PlayerController] Left deposit range — drain stopped.");
            }
        }
    }

    /// <summary>
    /// Finds the closest DepositStation within StationDepositRadius.
    /// Returns null if none are in range.
    /// </summary>
    private DepositStation FindClosestStation()
    {
        DepositStation closest = null;
        float minDist = GameConstants.StationDepositRadius;

        foreach (var station in
            FindObjectsByType<DepositStation>(FindObjectsSortMode.None))
        {
            // Skip full stations — no point draining into a complete zone.
            if (station.Progress.Value >= GameConstants.StationFillThreshold) continue;

            float dist = Vector3.Distance(
                transform.position, station.transform.position);

            if (dist < minDist)
            {
                minDist = dist;
                closest = station;
            }
        }
        return closest;
    }

    /// <summary>
    /// Coroutine that fires DepositTickServerRpc every DepositTickInterval
    /// while the player remains in range of _nearbyStation.
    /// Automatically stops if the station fills or the player has no resources.
    /// The range check in DepositTickServerRpc on the server is the authority —
    /// this coroutine is a convenience driver, not the guard.
    /// </summary>
    private IEnumerator DepositDrainCoroutine()
    {
        while (_nearbyStation != null)
        {
            // Local early-out checks — reduce unnecessary RPCs.
            if (networkPlayer != null && networkPlayer.resourceCount.Value <= 0)
            {
                Debug.Log("[PlayerController] No resources to deposit — drain paused.");
                yield return new WaitForSeconds(GameConstants.DepositTickInterval);
                continue;
            }

            if (_nearbyStation.Progress.Value >= GameConstants.StationFillThreshold)
            {
                Debug.Log("[PlayerController] Station full — drain stopped.");
                StopDepositCoroutine();
                yield break;
            }

            _nearbyStation.DepositTickServerRpc();
            yield return new WaitForSeconds(GameConstants.DepositTickInterval);
        }
    }

    private void StopDepositCoroutine()
    {
        if (_depositCoroutine != null)
        {
            StopCoroutine(_depositCoroutine);
            _depositCoroutine = null;
        }
        _nearbyStation = null;
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
        CheckDepositProximity();
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