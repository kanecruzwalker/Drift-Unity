using System;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// NetworkPlayer — per-player networked state for all connected clients.
///
/// One instance exists per connected player, owned by that player's client.
/// All NetworkVariables are written by the owning client or the server,
/// and read by all other clients automatically via Netcode for GameObjects.
///
/// Data model (all baked in for full post-submission flexibility):
///   Core state    — health, resources, position, color, ready flag
///   Inventory     — 6 ItemSlot slots, manual equip/unequip
///   Effects       — 4 ActiveEffect slots, timed buff/debuff
///   Hired NPCs    — 3 HireContract slots, session-duration companions
///
/// NetworkVariable write permissions:
///   health, activeEffects, hiredNPCs — server writes only
///   resourceCount, inventory         — owner writes (via ServerRpc validation)
///   worldPosition, playerColor       — owner writes
///   isReady                          — owner writes
///
/// Post-submission systems that read this data:
///   UIFeedback     — worldPosition feeds the 4-finger beam direction
///   MiniMap        — worldPosition feeds player dot rendering
///   HUD            — resourceCount, health feed partner status panel
///   AllyNPC        — hiredNPCs[].ownerPlayerId feeds follow targeting
///   ZoneShop       — writes to inventory and activeEffects on purchase
/// </summary>
public class NetworkPlayer : NetworkBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // NETWORK VARIABLES — Core State
    // ─────────────────────────────────────────────────────────────────────────

    /// Current health. Server writes on damage/heal. Range: 0..PlayerMaxHealth.
    public NetworkVariable<float> health = new NetworkVariable<float>(
        GameConstants.PlayerMaxHealth,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// Individual resource count. Owner requests changes via ServerRpc.
    public NetworkVariable<int> resourceCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// World-space position, updated by owner each frame.
    /// Consumed by UIFeedback (beam direction) and MiniMap (dot position).
    public NetworkVariable<Vector3> worldPosition = new NetworkVariable<Vector3>(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// Assigned color for this player — set once on join, never changes.
    /// Drives PlayerVisuals trail and HUD partner indicator color.
    public NetworkVariable<Color> playerColor = new NetworkVariable<Color>(
        Color.white,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// Lobby ready state. Owner sets true when ready for host to start.
    public NetworkVariable<bool> isReady = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    // ─────────────────────────────────────────────────────────────────────────
    // NETWORK VARIABLES — Inventory
    // ─────────────────────────────────────────────────────────────────────────

    /// 6-slot inventory. Server validates and writes on equip/unequip/pickup.
    /// Initialized to all empty slots.
    /// FUTURE: InventoryUI reads this to render the slot grid.
    public NetworkVariable<NetworkSerializableItemSlots> inventory =
        new NetworkVariable<NetworkSerializableItemSlots>(
            new NetworkSerializableItemSlots(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    // NETWORK VARIABLES — Active Effects
    // ─────────────────────────────────────────────────────────────────────────

    /// Up to 4 simultaneous timed effects. Server writes on purchase and tick.
    /// FUTURE: GameManager ticks remainingTime down each frame server-side.
    public NetworkVariable<NetworkSerializableActiveEffects> activeEffects =
        new NetworkVariable<NetworkSerializableActiveEffects>(
            new NetworkSerializableActiveEffects(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    // NETWORK VARIABLES — Hired NPCs
    // ─────────────────────────────────────────────────────────────────────────

    /// Up to 3 simultaneously hired NPC companions. Server writes on hire/expiry.
    /// FUTURE: AllyNPC reads ownerPlayerId to determine follow target.
    public NetworkVariable<NetworkSerializableHireContracts> hiredNPCs =
        new NetworkVariable<NetworkSerializableHireContracts>(
            new NetworkSerializableHireContracts(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    // ─────────────────────────────────────────────────────────────────────────
    // UNITY LIFECYCLE
    // ─────────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // Assign a unique color based on client ID using golden angle HSL
            // distribution — same approach used in PlayAI balloon color system.
            float hue = (OwnerClientId * 137.508f) % 360f / 360f;
            playerColor.Value = Color.HSVToRGB(hue, 0.7f, 0.9f);

            Debug.Log($"[NetworkPlayer] Spawned as owner. ClientId={OwnerClientId} " +
                      $"Color={playerColor.Value}");
        }

        // Subscribe to health changes for death detection on all clients.
        health.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POSITION SYNC
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        // Owner updates world position every frame so other systems
        // (UIFeedback beam, MiniMap dot) always have a current position.
        if (IsOwner)
            worldPosition.Value = transform.position;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SERVER RPCs — Resource Management
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Request the server to add resources to this player's count.
    /// Called by PlayerController when a collect is confirmed server-side.
    /// Server validates the amount before applying.
    /// </summary>
    [ServerRpc]
    public void AddResourcesServerRpc(int amount)
    {
        if (amount <= 0) return;
        resourceCount.Value += amount;
        Debug.Log($"[NetworkPlayer] ClientId={OwnerClientId} resources: {resourceCount.Value}");
    }

    /// <summary>
    /// Request the server to spend resources from this player's count.
    /// Called by DepositStation and ZoneShop when the player interacts.
    /// Server validates sufficient funds before applying.
    /// </summary>
    /// <returns>True if the server applied the spend (sufficient resources).</returns>
    [ServerRpc]
    public void SpendResourcesServerRpc(int amount, ServerRpcParams rpcParams = default)
    {
        if (amount <= 0 || resourceCount.Value < amount)
        {
            Debug.LogWarning($"[NetworkPlayer] SpendResources rejected. " +
                             $"Requested={amount} Available={resourceCount.Value}");
            return;
        }

        resourceCount.Value -= amount;
        Debug.Log($"[NetworkPlayer] ClientId={OwnerClientId} spent {amount}. " +
                  $"Remaining={resourceCount.Value}");
    }

    /// <summary>
    /// Request the server to transfer resources from this player to a partner.
    /// Triggered by the 4-finger share gesture in PlayerController.
    /// Server validates both players exist and caller has sufficient resources.
    /// </summary>
    [ServerRpc]
    public void ShareResourcesServerRpc(ulong targetPlayerId, int amount)
    {
        if (amount <= 0 || resourceCount.Value < amount) return;

        NetworkObject targetNetObj;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(targetPlayerId, out targetNetObj)) return;

        NetworkPlayer target = targetNetObj.GetComponent<NetworkPlayer>();
        if (target == null) return;

        resourceCount.Value -= amount;
        target.resourceCount.Value += amount;

        Debug.Log($"[NetworkPlayer] ClientId={OwnerClientId} shared {amount} " +
                  $"resources with ClientId={target.OwnerClientId}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SERVER RPCs — Ready State
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Toggle this player's lobby ready state.
    /// Called by LobbyUI when the player taps the ready button.
    /// </summary>
    [ServerRpc]
    public void SetReadyServerRpc(bool ready)
    {
        isReady.Value = ready;
        Debug.Log($"[NetworkPlayer] ClientId={OwnerClientId} ready={ready}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HEALTH / DAMAGE
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Apply damage to this player. Server-only — called by IDamageable
    /// implementors after ServerRpc validation.
    /// Clamps health to zero and calls HandleDeath when health reaches 0.
    /// </summary>
    public void ApplyDamage(float amount, ulong sourceId)
    {
        if (!IsServer) return;
        health.Value = Mathf.Max(0f, health.Value - amount);
        if (health.Value <= 0f)
            HandleDeath(sourceId);
    }

    /// <summary>
    /// Restore health. Server-only. Clamps to PlayerMaxHealth.
    /// FUTURE: called by consumable items and ally NPC heal abilities.
    /// </summary>
    public void ApplyHeal(float amount)
    {
        if (!IsServer) return;
        health.Value = Mathf.Min(GameConstants.PlayerMaxHealth,
                                 health.Value + amount);
    }

    /// <summary>
    /// Called when health reaches zero. Handles death state.
    /// FUTURE: trigger respawn timer, drop held resources, notify GameManager.
    /// </summary>
    private void HandleDeath(ulong sourceId)
    {
        Debug.Log($"[NetworkPlayer] ClientId={OwnerClientId} died. " +
                  $"KilledBy NetworkObjectId={sourceId}");
        // FUTURE: StartCoroutine(RespawnAfterDelay(GameConstants.RespawnDelay));
        // FUTURE: DropResources();
        // FUTURE: GameManager.Instance.OnPlayerDeath(OwnerClientId);
    }

    /// <summary>
    /// Health change callback — fires on all clients when health NetworkVariable
    /// changes. Used for damage flash visual via PlayerVisuals (post-submission).
    /// </summary>
    private void OnHealthChanged(float previous, float current)
    {
        if (current < previous)
            Debug.Log($"[NetworkPlayer] ClientId={OwnerClientId} took damage. " +
                      $"Health={current:F1}/{GameConstants.PlayerMaxHealth}");
        // FUTURE: if (IsOwner) playerVisuals.TriggerDamageFlash();
    }



    /// <summary>
    /// Sets the player's resource count directly. Called server-side by
    /// DepositStation when the player deposits all resources.
    /// Only callable on the server — NetworkVariable write permission is Server.
    /// </summary>
    public void SetResourceCount(int count)
    {
        if (!IsServer) return;
        resourceCount.Value = Mathf.Max(0, count);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// NETWORK-SERIALIZABLE WRAPPERS
// NetworkVariable requires INetworkSerializable. These thin wrappers give our
// plain C# struct arrays the serialization Unity Netcode needs.
// Each wrapper holds a fixed-size array matching the GameConstants cap.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Serializable wrapper for the 6-slot ItemSlot inventory array.</summary>
public struct NetworkSerializableItemSlots : INetworkSerializable, IEquatable<NetworkSerializableItemSlots>
{
    public ItemSlot slot0, slot1, slot2, slot3, slot4, slot5;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref slot0.itemType);
        serializer.SerializeValue(ref slot0.isEquipped);
        serializer.SerializeValue(ref slot1.itemType);
        serializer.SerializeValue(ref slot1.isEquipped);
        serializer.SerializeValue(ref slot2.itemType);
        serializer.SerializeValue(ref slot2.isEquipped);
        serializer.SerializeValue(ref slot3.itemType);
        serializer.SerializeValue(ref slot3.isEquipped);
        serializer.SerializeValue(ref slot4.itemType);
        serializer.SerializeValue(ref slot4.isEquipped);
        serializer.SerializeValue(ref slot5.itemType);
        serializer.SerializeValue(ref slot5.isEquipped);
    }

    public bool Equals(NetworkSerializableItemSlots other) =>
        slot0.Equals(other.slot0) && slot1.Equals(other.slot1) &&
        slot2.Equals(other.slot2) && slot3.Equals(other.slot3) &&
        slot4.Equals(other.slot4) && slot5.Equals(other.slot5);
}

/// <summary>Serializable wrapper for the 4-slot ActiveEffect array.</summary>
public struct NetworkSerializableActiveEffects : INetworkSerializable, IEquatable<NetworkSerializableActiveEffects>
{
    public ActiveEffect effect0, effect1, effect2, effect3;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref effect0.effectType);
        serializer.SerializeValue(ref effect0.modifier);
        serializer.SerializeValue(ref effect0.remainingTime);
        serializer.SerializeValue(ref effect1.effectType);
        serializer.SerializeValue(ref effect1.modifier);
        serializer.SerializeValue(ref effect1.remainingTime);
        serializer.SerializeValue(ref effect2.effectType);
        serializer.SerializeValue(ref effect2.modifier);
        serializer.SerializeValue(ref effect2.remainingTime);
        serializer.SerializeValue(ref effect3.effectType);
        serializer.SerializeValue(ref effect3.modifier);
        serializer.SerializeValue(ref effect3.remainingTime);
    }

    public bool Equals(NetworkSerializableActiveEffects other) =>
        effect0.Equals(other.effect0) && effect1.Equals(other.effect1) &&
        effect2.Equals(other.effect2) && effect3.Equals(other.effect3);
}

/// <summary>Serializable wrapper for the 3-slot HireContract array.</summary>
public struct NetworkSerializableHireContracts : INetworkSerializable, IEquatable<NetworkSerializableHireContracts>
{
    public HireContract contract0, contract1, contract2;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref contract0.npcType);
        serializer.SerializeValue(ref contract0.cost);
        serializer.SerializeValue(ref contract0.remainingDuration);
        serializer.SerializeValue(ref contract0.ownerPlayerId);
        serializer.SerializeValue(ref contract1.npcType);
        serializer.SerializeValue(ref contract1.cost);
        serializer.SerializeValue(ref contract1.remainingDuration);
        serializer.SerializeValue(ref contract1.ownerPlayerId);
        serializer.SerializeValue(ref contract2.npcType);
        serializer.SerializeValue(ref contract2.cost);
        serializer.SerializeValue(ref contract2.remainingDuration);
        serializer.SerializeValue(ref contract2.ownerPlayerId);
    }

    public bool Equals(NetworkSerializableHireContracts other) =>
        contract0.Equals(other.contract0) &&
        contract1.Equals(other.contract1) &&
        contract2.Equals(other.contract2);
}