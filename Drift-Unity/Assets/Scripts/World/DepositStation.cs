using Unity.Netcode;
using UnityEngine;

/// <summary>
/// DepositStation — a shared deposit point that players fill with resources
/// to transition a zone from Contested → Safe.
///
/// Server-authoritative: only the server writes progress.
/// Clients send DepositServerRpc, server validates and increments.
///
/// Zone transition cascade (ADR-014):
///   progress >= StationContestedThreshold → WorldManager sets zone Contested
///   progress >= StationFillThreshold      → WorldManager sets zone Safe
///     · SpawnTick stops spawning enemies in that zone
///     · DomainZone reveals visual, removes hazard
///     · ZoneShop activates
///     · Adjacent zones unlock
///
/// Implements IInteractable — players deposit by walking into interact radius
/// and the interaction is triggered automatically when in range.
///
/// Attach to: DepositStation prefab with NetworkObject component.
/// </summary>
public class DepositStation : NetworkBehaviour, IInteractable
{
    // ─────────────────────────────────────────────────────────────────────────
    // NETWORKED STATE
    // ─────────────────────────────────────────────────────────────────────────

    /// Current deposit progress — written server-side, read by all clients.
    /// Drives visual fill on DomainZone and HUD progress bars.
    public NetworkVariable<int> Progress { get; } =
        new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    /// Index of the zone this station belongs to — set at spawn.
    public NetworkVariable<int> ZoneIndex { get; } =
        new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("ZoneShop component on this GameObject — activated when zone becomes Safe.")]
    [SerializeField] private ZoneShop zoneShop;

    [Tooltip("Renderer for the station visual — color driven by progress.")]
    [SerializeField] private Renderer stationRenderer;

    // ─────────────────────────────────────────────────────────────────────────
    // COLORS — matches the dark space / bioluminescent palette
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Color ColorEmpty = new Color(0.2f, 0.2f, 0.3f, 1f);
    private static readonly Color ColorContested = new Color(1.0f, 0.6f, 0.0f, 1f);
    private static readonly Color ColorSafe = new Color(0.25f, 0.88f, 0.82f, 1f);

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        Progress.OnValueChanged += OnProgressChanged;

        if (zoneShop == null)
            zoneShop = GetComponent<ZoneShop>();
        if (stationRenderer == null)
            stationRenderer = GetComponent<Renderer>();

        UpdateVisual(Progress.Value);
    }

    public override void OnNetworkDespawn()
    {
        Progress.OnValueChanged -= OnProgressChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IINTERACTABLE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when a player enters the deposit radius.
    /// Deposits all resources the player is carrying into the station.
    /// Server validates the deposit and updates Progress NetworkVariable.
    /// </summary>
    public void OnInteract(ulong playerId)
    {
        if (!IsServer) return;
        NetworkPlayer player = GetPlayerById(playerId);
        if (player == null) return;

        int amount = player.resourceCount.Value;
        if (amount <= 0) return;

        // Deduct resources from player.
        player.SetResourceCount(0);

        // Add to station progress — capped at fill threshold.
        int newProgress = Mathf.Min(
            Progress.Value + amount,
            GameConstants.StationFillThreshold);
        Progress.Value = newProgress;

        Debug.Log($"[DepositStation] Player {playerId} deposited {amount}. " +
                  $"Progress={newProgress}/{GameConstants.StationFillThreshold}");

        // Check for zone state transitions.
        CheckZoneTransition(newProgress);
    }

    public string GetInteractLabel()
    {
        if (Progress.Value >= GameConstants.StationFillThreshold)
            return "Full";
        return $"Deposit ({Progress.Value}/{GameConstants.StationFillThreshold})";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SERVER RPC — client-initiated deposit request
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PlayerController when the player enters deposit radius.
    /// Server validates and calls OnInteract with the sender's client ID.
    /// </summary>
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestDepositServerRpc(RpcParams rpcParams = default)
    {
        float dist = Vector3.Distance(
            transform.position,
            GetPlayerById(rpcParams.Receive.SenderClientId)?.transform.position
            ?? Vector3.one * 9999f);

        if (dist <= GameConstants.StationDepositRadius * 2f)
            OnInteract(rpcParams.Receive.SenderClientId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ZONE TRANSITION
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks progress thresholds and notifies WorldManager to update ZoneState.
    /// Called server-side after every deposit.
    /// Cascade: WorldManager.SetZoneState() → DomainZone reacts → shop opens.
    /// </summary>
    private void CheckZoneTransition(int progress)
    {
        if (!IsServer || ZoneIndex.Value < 0) return;

        if (progress >= GameConstants.StationFillThreshold)
        {
            WorldManager.Instance?.SetZoneState(ZoneIndex.Value, ZoneState.Safe);
            Debug.Log($"[DepositStation] Zone {ZoneIndex.Value} → Safe");
        }
        else if (progress >= GameConstants.StationContestedThreshold)
        {
            WorldManager.Instance?.SetZoneState(ZoneIndex.Value, ZoneState.Contested);
            Debug.Log($"[DepositStation] Zone {ZoneIndex.Value} → Contested");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VISUAL
    // ─────────────────────────────────────────────────────────────────────────

    private void OnProgressChanged(int previous, int current)
    {
        UpdateVisual(current);

        // Open shop when zone becomes Safe.
        if (current >= GameConstants.StationFillThreshold && zoneShop != null)
            zoneShop.SetOpen(true);
    }

    /// <summary>
    /// Updates the station material color based on fill progress.
    /// Empty → dark gray, Contested → amber, Safe → teal.
    /// </summary>
    private void UpdateVisual(int progress)
    {
        if (stationRenderer == null) return;

        Color target;
        if (progress >= GameConstants.StationFillThreshold)
            target = ColorSafe;
        else if (progress >= GameConstants.StationContestedThreshold)
            target = ColorContested;
        else
            target = ColorEmpty;

        stationRenderer.material.color = target;

        if (stationRenderer.material.HasProperty("_EmissionColor"))
        {
            stationRenderer.material.EnableKeyword("_EMISSION");
            stationRenderer.material.SetColor("_EmissionColor", target * 1.5f);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    private NetworkPlayer GetPlayerById(ulong clientId)
    {
        foreach (var player in
            FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player.OwnerClientId == clientId)
                return player;
        }
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUBLIC API — called by WorldManager at spawn
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initializes this station with its zone index. Called by WorldManager
    /// after spawning — sets ZoneIndex NetworkVariable server-side.
    /// </summary>
    public void Initialize(int zoneIndex)
    {
        if (!IsServer) return;
        ZoneIndex.Value = zoneIndex;
    }
}