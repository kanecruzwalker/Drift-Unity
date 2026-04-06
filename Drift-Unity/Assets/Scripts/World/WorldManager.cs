using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// WorldManager — server-authoritative world state and orb spawning.
///
/// Responsibilities:
///   · Generate world layout from worldSeed (ADR-015)
///   · Maintain zoneStates[] NetworkVariable — single source of truth (ADR-014)
///   · Run SpawnTick() on server — spawn orbs per zone based on SpawnRateTable
///   · Provide SetZoneState() and GetZoneState() to other systems
///   · Manage DepositStation references per zone
///   · Apply world soft-boundary (player boundary handled in PlayerController)
///
/// World layout:
///   ZoneGridSize x ZoneGridSize grid of zones, each zone is a square region.
///   Zone 0 = center starting zone (always Safe at session start).
///   All other zones start Undiscovered.
///   Zone size = (WorldHalfExtent * 2) / ZoneGridSize per axis.
///
/// Networking:
///   worldSeed and zoneStates are NetworkVariables — synced to all clients.
///   Clients read GetZoneState() to drive DomainZone visuals locally.
///   Only server calls SetZoneState() and SpawnTick().
///
/// Attach to: WorldManager GameObject in SampleScene.
/// There should be exactly one WorldManager in the scene.
/// </summary>
public class WorldManager : NetworkBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // SINGLETON
    // ─────────────────────────────────────────────────────────────────────────

    public static WorldManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NETWORKED STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Seed for deterministic world layout generation (ADR-015).
    /// Set by host at session start — all clients generate identical layout.
    public NetworkVariable<int> worldSeed { get; } =
        new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// Game mode — drives whether hazard orbs deal damage.
    public NetworkVariable<int> gameMode { get; } =
        new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// Hazard expansion factor — grows over time in PvE mode.
    public NetworkVariable<float> hazardExpansion { get; } =
        new NetworkVariable<float>(0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    // ZONE STATE — local array mirroring server state
    // ─────────────────────────────────────────────────────────────────────────

    /// Zone states — server writes, clients read via GetZoneState().
    /// Index = zoneX + zoneZ * ZoneGridSize (row-major).
    private ZoneState[] _zoneStates;

    /// Total number of zones in the grid.
    private int ZoneCount => GameConstants.ZoneGridSize * GameConstants.ZoneGridSize;

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("ScriptableObjects")]
    [Tooltip("Spawn rate table — assign SpawnRateTable asset in Inspector.")]
    [SerializeField] private SpawnRateTable spawnRateTable;

    [Header("Prefabs")]
    [Tooltip("ResourceOrb prefab — must have NetworkObject component.")]
    [SerializeField] private GameObject resourceOrbPrefab;

    [Tooltip("DepositStation prefab — must have NetworkObject component.")]
    [SerializeField] private GameObject depositStationPrefab;

    // ─────────────────────────────────────────────────────────────────────────
    // RUNTIME STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Active orbs per zone — tracked to enforce MaxOrbsPerZone.
    private Dictionary<int, List<NetworkObject>> _orbsByZone
        = new Dictionary<int, List<NetworkObject>>();

    /// Deposit stations per zone — one station per zone.
    private Dictionary<int, DepositStation> _stationsByZone
        = new Dictionary<int, DepositStation>();

    /// Spawn tick coroutine handle.
    private Coroutine _spawnTickCoroutine;

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        InitializeZoneStates();

        if (IsServer)
        {
            // Generate world seed and broadcast to all clients.
            worldSeed.Value = Random.Range(1, int.MaxValue);
            Random.InitState(worldSeed.Value);

            Debug.Log($"[WorldManager] World seed: {worldSeed.Value}");

            // Zone 0 (center) starts Safe — players spawn here.
            SetZoneState(0, ZoneState.Safe);

            // Spawn deposit stations for all zones.
            SpawnDepositStations();

            // Start the spawn tick.
            _spawnTickCoroutine = StartCoroutine(SpawnTickLoop());

            Debug.Log("[WorldManager] Server initialized. SpawnTick started.");
        }
    }

    public override void OnNetworkDespawn()
    {
        if (_spawnTickCoroutine != null)
            StopCoroutine(_spawnTickCoroutine);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ZONE STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes zone state array. Zone 0 = Safe, all others = Undiscovered.
    /// Called on both server and client — clients maintain local copy for DomainZone.
    /// </summary>
    private void InitializeZoneStates()
    {
        _zoneStates = new ZoneState[ZoneCount];
        for (int i = 0; i < ZoneCount; i++)
            _zoneStates[i] = ZoneState.Undiscovered;

        // Center zone starts Safe.
        int centerIndex = ZoneCount / 2;
        _zoneStates[centerIndex] = ZoneState.Safe;
    }

    /// <summary>
    /// Returns the current state of a zone by index.
    /// Called by DomainZone each frame to poll for visual state changes.
    /// </summary>
    public ZoneState GetZoneState(int zoneIndex)
    {
        if (_zoneStates == null || zoneIndex < 0 || zoneIndex >= ZoneCount)
            return ZoneState.Undiscovered;
        return _zoneStates[zoneIndex];
    }

    /// <summary>
    /// Sets the state of a zone — server-authoritative.
    /// Called by DepositStation when progress thresholds are crossed.
    ///
    /// Cascade effects:
    ///   → ZoneState.Safe: opens ZoneShop, stops enemy spawning in zone
    ///   → ZoneState.Contested: increases orb yield, enemies patrol
    ///   → Unlocks adjacent Undiscovered zones → Discovered
    /// </summary>
    public void SetZoneState(int zoneIndex, ZoneState state)
    {
        if (!IsServer) return;
        if (_zoneStates == null || zoneIndex < 0 || zoneIndex >= ZoneCount) return;

        ZoneState previous = _zoneStates[zoneIndex];
        _zoneStates[zoneIndex] = state;

        Debug.Log($"[WorldManager] Zone {zoneIndex}: {previous} → {state}");

        // When a zone becomes Safe, reveal adjacent zones.
        if (state == ZoneState.Safe)
            UnlockAdjacentZones(zoneIndex);

        // Sync state to all clients via ClientRpc.
        SyncZoneStateClientRpc(zoneIndex, (int)state);
    }

    /// <summary>
    /// Syncs a single zone state change to all clients.
    /// Called by SetZoneState() after server updates the local array.
    /// </summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void SyncZoneStateClientRpc(int zoneIndex, int stateInt)
    {
        if (_zoneStates == null) InitializeZoneStates();
        _zoneStates[zoneIndex] = (ZoneState)stateInt;
        Debug.Log($"[WorldManager] Client: Zone {zoneIndex} → {(ZoneState)stateInt}");
    }

    /// <summary>
    /// When a zone becomes Safe, adjacent Undiscovered zones become Discovered.
    /// This is the domain expansion mechanic — securing zones reveals new territory.
    /// </summary>
    private void UnlockAdjacentZones(int zoneIndex)
    {
        int gridSize = GameConstants.ZoneGridSize;
        int x = zoneIndex % gridSize;
        int z = zoneIndex / gridSize;

        int[] dx = { -1, 1, 0, 0 };
        int[] dz = { 0, 0, -1, 1 };

        for (int i = 0; i < 4; i++)
        {
            int nx = x + dx[i];
            int nz = z + dz[i];

            if (nx < 0 || nx >= gridSize || nz < 0 || nz >= gridSize)
                continue;

            int adjacentIndex = nx + nz * gridSize;
            if (_zoneStates[adjacentIndex] == ZoneState.Undiscovered)
            {
                SetZoneState(adjacentIndex, ZoneState.Discovered);
                Debug.Log($"[WorldManager] Zone {adjacentIndex} unlocked → Discovered");
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPAWN TICK
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs SpawnTick() on the server every SpawnTickInterval seconds.
    /// Spawns orbs in each zone based on SpawnRateTable and current ZoneState.
    /// </summary>
    private IEnumerator SpawnTickLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(GameConstants.SpawnTickInterval);
            SpawnTick();
        }
    }

    /// <summary>
    /// One spawn tick — iterates all zones and spawns orbs based on rates.
    /// Only runs on server. Uses SpawnRateTable to get rates per ZoneState.
    /// Respects MaxOrbsPerZone cap.
    /// </summary>
    private void SpawnTick()
    {
        if (spawnRateTable == null) return;

        for (int i = 0; i < ZoneCount; i++)
        {
            ZoneState state = _zoneStates[i];

            // Count active orbs in this zone.
            int activeOrbs = GetActiveOrbCount(i);
            if (activeOrbs >= GameConstants.MaxOrbsPerZone) continue;

            // Roll against spawn rate.
            float rate = spawnRateTable.GetOrbRate(state);
            if (Random.value > rate) continue;

            // Pick orb type based on zone state.
            OrbType orbType = PickOrbType(state);
            SpawnOrb(i, orbType);
        }
    }

    /// <summary>
    /// Picks an orb type appropriate for the zone state.
    /// Undiscovered/Discovered zones favor Guide orbs to help exploration.
    /// Contested zones have Patrol orbs.
    /// Hazard orbs only spawn in PvE mode.
    /// </summary>
    private OrbType PickOrbType(ZoneState state)
    {
        bool isPvE = (GameMode)gameMode.Value == GameMode.PvE;

        return state switch
        {
            ZoneState.Undiscovered => Random.value < 0.3f
                ? OrbType.Guide : OrbType.Stationary,
            ZoneState.Discovered => Random.value < 0.2f
                ? OrbType.Guide : OrbType.Stationary,
            ZoneState.Contested => isPvE && Random.value < 0.25f
                ? OrbType.Hazard
                : Random.value < 0.4f ? OrbType.Patrol : OrbType.Stationary,
            ZoneState.Safe => OrbType.Stationary,
            _ => OrbType.Stationary
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SPAWN HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a ResourceOrb in the given zone at a random position within
    /// that zone's bounds. Initializes the orb with type and zone index.
    /// Server-authoritative — NetworkObject.Spawn() replicates to clients.
    /// </summary>
    private void SpawnOrb(int zoneIndex, OrbType type)
    {
        if (resourceOrbPrefab == null) return;

        Vector3 pos = GetRandomPositionInZone(zoneIndex);
        pos.y = 0.5f; // Float just above ground plane.

        GameObject go = Instantiate(resourceOrbPrefab, pos, Quaternion.identity);
        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null) { Destroy(go); return; }

        netObj.Spawn(destroyWithScene: true);

        ResourceOrb orb = go.GetComponent<ResourceOrb>();
        orb?.Initialize(type, zoneIndex);

        // Track in zone orb list.
        if (!_orbsByZone.ContainsKey(zoneIndex))
            _orbsByZone[zoneIndex] = new List<NetworkObject>();
        _orbsByZone[zoneIndex].Add(netObj);

        Debug.Log($"[WorldManager] Spawned {type} orb in zone {zoneIndex} at {pos}");
    }

    /// <summary>
    /// Spawns one DepositStation per zone at the zone center.
    /// Called once at session start by the server.
    /// </summary>
    private void SpawnDepositStations()
    {
        if (depositStationPrefab == null)
        {
            Debug.LogWarning("[WorldManager] No DepositStation prefab assigned.");
            return;
        }

        for (int i = 0; i < ZoneCount; i++)
        {
            Vector3 center = GetZoneCenter(i);
            center.y = 0.1f;

            GameObject go = Instantiate(
                depositStationPrefab, center, Quaternion.identity);
            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj == null) { Destroy(go); continue; }

            netObj.Spawn(destroyWithScene: true);

            DepositStation station = go.GetComponent<DepositStation>();
            station?.Initialize(i);

            _stationsByZone[i] = station;
        }

        Debug.Log($"[WorldManager] Spawned {ZoneCount} deposit stations.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ZONE GEOMETRY
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the world-space center position of a zone by index.
    /// Zone layout is a ZoneGridSize x ZoneGridSize grid centered at origin.
    /// </summary>
    public Vector3 GetZoneCenter(int zoneIndex)
    {
        int gridSize = GameConstants.ZoneGridSize;
        float zoneSize = (GameConstants.WorldHalfExtent * 2f) / gridSize;

        int x = zoneIndex % gridSize;
        int z = zoneIndex / gridSize;

        float worldX = -GameConstants.WorldHalfExtent + (x + 0.5f) * zoneSize;
        float worldZ = -GameConstants.WorldHalfExtent + (z + 0.5f) * zoneSize;

        return new Vector3(worldX, 0f, worldZ);
    }

    /// <summary>
    /// Returns a random world-space position within a zone's bounds.
    /// Used by SpawnOrb to distribute orbs across the zone area.
    /// </summary>
    private Vector3 GetRandomPositionInZone(int zoneIndex)
    {
        int gridSize = GameConstants.ZoneGridSize;
        float zoneSize = (GameConstants.WorldHalfExtent * 2f) / gridSize;
        float margin = 2f; // Keep orbs away from zone edges.

        int x = zoneIndex % gridSize;
        int z = zoneIndex / gridSize;

        float minX = -GameConstants.WorldHalfExtent + x * zoneSize + margin;
        float maxX = minX + zoneSize - margin * 2f;
        float minZ = -GameConstants.WorldHalfExtent + z * zoneSize + margin;
        float maxZ = minZ + zoneSize - margin * 2f;

        // Perlin noise offset breaks square clustering — orbs feel organically placed.
        // Seeded by grid position so layout is consistent with worldSeed determinism.
        float noiseX = (Mathf.PerlinNoise(x * 0.3f, z * 0.3f) - 0.5f) * zoneSize * 0.3f;
        float noiseZ = (Mathf.PerlinNoise(x * 0.3f + 100f, z * 0.3f + 100f) - 0.5f)
                       * zoneSize * 0.3f;

        return new Vector3(
            Mathf.Clamp(Random.Range(minX, maxX) + noiseX, minX, maxX),
            0.5f,
            Mathf.Clamp(Random.Range(minZ, maxZ) + noiseZ, minZ, maxZ));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UTILITY
    // ─────────────────────────────────────────────────────────────────────────

    private int GetActiveOrbCount(int zoneIndex)
    {
        if (!_orbsByZone.ContainsKey(zoneIndex)) return 0;

        // Clean up destroyed orbs from the list.
        _orbsByZone[zoneIndex].RemoveAll(o => o == null);
        return _orbsByZone[zoneIndex].Count;
    }

    /// <summary>
    /// Returns the zone index for a given world-space position.
    /// Used by players and systems to determine which zone they are in.
    /// </summary>
    public int GetZoneIndexForPosition(Vector3 worldPos)
    {
        int gridSize = GameConstants.ZoneGridSize;
        float zoneSize = (GameConstants.WorldHalfExtent * 2f) / gridSize;

        int x = Mathf.Clamp(
            (int)((worldPos.x + GameConstants.WorldHalfExtent) / zoneSize),
            0, gridSize - 1);
        int z = Mathf.Clamp(
            (int)((worldPos.z + GameConstants.WorldHalfExtent) / zoneSize),
            0, gridSize - 1);

        return x + z * gridSize;
    }
}