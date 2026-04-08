using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ResourceOrb — a collectible resource object in the world.
///
/// Server-authoritative collection (ADR-004):
///   Client sends RequestCollectServerRpc → server validates proximity
///   → awards resources to NetworkPlayer → despawns the orb NetworkObject.
///
/// OrbType drives behavior and visual identity:
///   Stationary — floats/bobs in place, most common
///   Patrol     — follows waypoint path, requires timing
///   Guide      — drifts toward undiscovered zone edges
///   Hazard     — damages on contact in PvE (visual only in Passive)
///
/// Visual: bioluminescent glow matching the space/forest palette.
///   Colors per type are defined in the static palette below.
///   Breathing pulse animation runs on all orb types.
///
/// Attach to: ResourceOrb prefab with NetworkObject component.
/// Spawned exclusively by WorldManager (server-authoritative).
/// </summary>
public class ResourceOrb : NetworkBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // NETWORKED STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Type of this orb — set at spawn by WorldManager, read by all clients.
    public NetworkVariable<OrbType> orbType { get; } =
        new NetworkVariable<OrbType>(OrbType.Stationary,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// Zone index this orb belongs to — used for despawn validation.
    public NetworkVariable<int> zoneIndex { get; } =
        new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Collection")]
    [Tooltip("Resources awarded to the collecting player.")]
    [SerializeField] private int resourceValue = 1;

    [Tooltip("Maximum distance from the orb the player can be to collect it.")]
    [SerializeField] private float collectRadius = 2.5f;

    [Header("Patrol Settings")]
    [Tooltip("Patrol waypoint radius around spawn point (Patrol orb type only).")]
    [SerializeField] private float patrolRadius = 6f;

    [Tooltip("Patrol movement speed (Patrol orb type only).")]
    [SerializeField] private float patrolSpeed = 2f;

    [Header("Visual")]
    [Tooltip("Renderer for the orb mesh — color driven by OrbType.")]
    [SerializeField] private Renderer orbRenderer;

    [Tooltip("Breathing pulse speed.")]
    [SerializeField] private float pulseSpeed = 1.8f;

    [Tooltip("Breathing pulse scale amount.")]
    [SerializeField] private float pulseAmount = 0.12f;

    // ─────────────────────────────────────────────────────────────────────────
    // ORB TYPE COLORS — bioluminescent space palette
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Color ColorStationary = new Color(0.72f, 0.83f, 1.00f); // soft blue-white
    private static readonly Color ColorPatrol = new Color(1.00f, 0.70f, 0.28f); // warm amber
    private static readonly Color ColorGuide = new Color(0.78f, 1.00f, 0.69f); // pale green
    private static readonly Color ColorHazard = new Color(1.00f, 0.27f, 0.33f); // deep crimson

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// World-space spawn position — Patrol orbs patrol around this point.
    private Vector3 _spawnPosition;

    /// Current patrol angle for Patrol orb circular movement.
    private float _patrolAngle = 0f;

    /// Whether this orb has been collected — prevents double-collect race.
    private bool _collected = false;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        _spawnPosition = transform.position;

        orbType.OnValueChanged += OnOrbTypeChanged;
        ApplyOrbTypeVisual(orbType.Value);

        // Randomize patrol start angle so orbs don't all move in sync.
        _patrolAngle = Random.Range(0f, 360f);
    }

    public override void OnNetworkDespawn()
    {
        orbType.OnValueChanged -= OnOrbTypeChanged;
    }

    private void Update()
    {
        UpdateBreathe();
        if (IsServer) UpdateBehavior();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SERVER-SIDE BEHAVIOR
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Drives orb movement behavior server-side based on OrbType.
    /// Only runs on the server — position is synced to clients via NetworkTransform.
    /// </summary>
    private void UpdateBehavior()
    {
        switch (orbType.Value)
        {
            case OrbType.Stationary:
                // Bob gently in place — Y oscillation only.
                float bobY = _spawnPosition.y +
                             Mathf.Sin(Time.time * pulseSpeed) * 0.3f;
                transform.position = new Vector3(
                    _spawnPosition.x, bobY, _spawnPosition.z);
                break;

            case OrbType.Patrol:
                // Circular patrol around spawn point.
                _patrolAngle += patrolSpeed * Time.deltaTime * 30f;
                float rad = _patrolAngle * Mathf.Deg2Rad;
                transform.position = _spawnPosition + new Vector3(
                    Mathf.Cos(rad) * patrolRadius,
                    Mathf.Sin(Time.time * pulseSpeed) * 0.3f,
                    Mathf.Sin(rad) * patrolRadius);
                break;

            case OrbType.Guide:
                // Drift slowly toward nearest undiscovered zone edge.
                // FUTURE: implement zone-edge pathfinding
                // For now: gentle drift in +X direction as placeholder
                transform.position += Vector3.right *
                    GameConstants.GuideOrbDriftSpeed * Time.deltaTime;
                break;

            case OrbType.Hazard:
                // Stationary in Passive mode — damages on contact in PvE.
                // Contact damage handled by OnTriggerEnter below.
                float hazBobY = _spawnPosition.y +
                                Mathf.Sin(Time.time * pulseSpeed * 1.5f) * 0.2f;
                transform.position = new Vector3(
                    _spawnPosition.x, hazBobY, _spawnPosition.z);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // COLLECTION — ServerRpc
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Client calls this to request collection from the server.
    /// Server validates:
    ///   1. Orb hasn't already been collected
    ///   2. Requesting player is within collectRadius
    ///   3. Orb is not a Hazard type (Hazard orbs aren't collectable)
    ///
    /// On success: awards resourceValue to player, despawns this NetworkObject.
    /// Collection is server-authoritative — no client-side prediction (ADR-004).
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestCollectServerRpc(RpcParams rpcParams = default)
    {
        if (_collected) return;
        if (orbType.Value == OrbType.Hazard) return;

        ulong senderId = rpcParams.Receive.SenderClientId;

        // Find the requesting player.
        NetworkPlayer player = null;
        foreach (var p in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (p.OwnerClientId == senderId) { player = p; break; }
        }

        if (player == null) return;

        // Validate proximity.
        float dist = Vector3.Distance(
            transform.position,
            player.worldPosition.Value);

        if (dist > collectRadius * 2f)
        {
            Debug.Log($"[ResourceOrb] Collect rejected — " +
                      $"player too far. dist={dist:F1} max={collectRadius * 2f:F1}");
            return;
        }

        // Award resources and despawn.
        _collected = true;
        int newCount = player.resourceCount.Value + resourceValue;
        player.SetResourceCount(newCount);

        Debug.Log($"[ResourceOrb] Collected by ClientId={senderId}. " +
                  $"+{resourceValue} resources → total={newCount}");

        // Despawn the NetworkObject — removes from all clients.
        NetworkObject.Despawn(destroy: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HAZARD CONTACT DAMAGE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Handles Hazard orb contact damage in PvE mode.
    /// Only runs server-side. Only applies damage in PvE game mode.
    /// Visual flash on contact runs client-side via the pulse animation.
    /// </summary>
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (orbType.Value != OrbType.Hazard) return;
        if (GameManager.Instance?.CurrentGameMode != GameMode.PvE) return;

        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null) return;

        player.TakeDamage(10f, NetworkObjectId);
        Debug.Log($"[ResourceOrb] Hazard contact damage → {other.name}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VISUAL
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gentle breathing scale pulse — runs on all clients for all orb types.
    /// Gives orbs an organic, living feel matching the bioluminescent aesthetic.
    /// </summary>
    private void UpdateBreathe()
    {
        float scale = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = Vector3.one * scale;
    }

    private void OnOrbTypeChanged(OrbType previous, OrbType current)
        => ApplyOrbTypeVisual(current);

    /// <summary>
    /// Applies the material color and emission for the given OrbType.
    /// Each type has a distinct color from the bioluminescent palette.
    /// </summary>
    private void ApplyOrbTypeVisual(OrbType type)
    {
        if (orbRenderer == null)
            orbRenderer = GetComponent<Renderer>();
        if (orbRenderer == null) return;

        Color color = type switch
        {
            OrbType.Stationary => ColorStationary,
            OrbType.Patrol => ColorPatrol,
            OrbType.Guide => ColorGuide,
            OrbType.Hazard => ColorHazard,
            _ => ColorStationary
        };

        orbRenderer.material.color = color;

        if (orbRenderer.material.HasProperty("_EmissionColor"))
        {
            orbRenderer.material.EnableKeyword("_EMISSION");
            orbRenderer.material.SetColor("_EmissionColor", color * 2f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC API — called by WorldManager at spawn
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes this orb with its type and zone. Called by WorldManager
    /// immediately after spawning. Server-side only.
    /// </summary>
    public void Initialize(OrbType type, int zone)
    {
        if (!IsServer) return;
        orbType.Value = type;
        zoneIndex.Value = zone;
        Debug.Log($"[ResourceOrb] Initialized. Type={type} Zone={zone}");
    }
}

/// <summary>
/// Orb types — drives spawn behavior, movement pattern, and visual identity.
/// Defined here as it's tightly coupled to ResourceOrb behavior.
/// Referenced by WorldManager spawn logic and SpawnRateTable.
/// </summary>
public enum OrbType
{
    Stationary,  // floats/bobs in place — most common, safe zones
    Patrol,      // circular waypoint path — requires timing to collect
    Guide,       // drifts toward undiscovered zone edges — leads exploration
    Hazard       // damages on contact in PvE — visual only in Passive
    // FUTURE: Weapon, Key, PuzzlePiece
}