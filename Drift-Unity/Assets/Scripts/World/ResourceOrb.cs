using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ResourceOrb — a collectible resource object in the world.
/// Full implementation in feature/world-layer.
/// </summary>
public class ResourceOrb : NetworkBehaviour
{
    public OrbType OrbType { get; private set; } = OrbType.Stationary;

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RequestCollectServerRpc(RpcParams rpcParams = default)
    {
        Debug.Log($"[ResourceOrb] Collect requested.");
    }
}

/// <summary>
/// Orb types — drives spawn behavior, movement, and visual identity.
/// Full enum used by WorldManager in feature/world-layer.
/// </summary>
public enum OrbType
{
    Stationary,
    Patrol,
    Guide,
    Hazard
    // FUTURE: Weapon, Key, PuzzlePiece
}