using Unity.Netcode;
using UnityEngine;

/// <summary>
/// WorldManager — server-authoritative world state and orb spawning.
/// Full implementation in feature/world-layer.
/// </summary>
public class WorldManager : NetworkBehaviour
{
    public static WorldManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
}