using Unity.Netcode;
using UnityEngine;

/// <summary>
/// EnemyOrb — hostile entity that patrols waypoints and damages players on contact.
///
/// STUB — class structure and interfaces defined. No AI behavior yet.
/// Full patrol behavior, damage dealing, and despawn on zone Safe
/// implemented in feature/game-loop.
///
/// Implements IEntity and IDamageable — registered in EntityStatTable.
/// Despawns when its zone transitions to ZoneState.Safe (ADR-014).
/// </summary>
public class EnemyOrb : NetworkBehaviour, IEntity, IDamageable
{
    public EntityType EntityType => EntityType.EnemyOrb;
    public Faction Faction => Faction.Hostile;

    public void TakeDamage(float amount, ulong sourceId)
    {
        // FUTURE: reduce health, despawn on zero, award resources to sourceId player
        Debug.Log($"[EnemyOrb] TakeDamage amount={amount} source={sourceId}");
    }

    public void OnDeath()
    {
        // FUTURE: play death VFX, drop bonus orb, despawn NetworkObject
        Debug.Log("[EnemyOrb] OnDeath");
    }
}