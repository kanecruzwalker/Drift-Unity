using Unity.Netcode;
using UnityEngine;

/// <summary>
/// AllyNPC — hired companion that assists the owning player.
///
/// STUB — class structure and interfaces defined. No behavior yet.
/// Full follow behavior, collect assist, and session cleanup
/// implemented post-submission.
///
/// Reads HireContract.ownerPlayerId to identify its owner.
/// Cleared from NetworkPlayer.hiredNPCs[] on death or session end.
/// </summary>
public class AllyNPC : NetworkBehaviour, IEntity, IDamageable
{
    public EntityType EntityType => EntityType.AllyNPC;
    public Faction Faction => Faction.Player;

    /// The player who hired this NPC — set by WorldManager on spawn.
    public ulong OwnerPlayerId { get; private set; }

    public void Initialize(ulong ownerPlayerId)
    {
        OwnerPlayerId = ownerPlayerId;
    }

    public void TakeDamage(float amount, ulong sourceId)
    {
        // FUTURE: reduce health, despawn, remove HireContract from owner's NetworkPlayer
        Debug.Log($"[AllyNPC] TakeDamage amount={amount} source={sourceId}");
    }

    public void OnDeath()
    {
        // FUTURE: play death VFX, despawn, clear HireContract
        Debug.Log("[AllyNPC] OnDeath");
    }
}