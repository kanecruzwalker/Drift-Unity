/// <summary>
/// IDamageable — implemented by any entity that can receive damage in Drift.
///
/// Current implementors: PlayerController
/// Future implementors:  EnemyOrb, AllyNPC, BossOrb (see ADR-013)
///
/// sourceId is the NetworkObjectId of the damage dealer — used for
/// kill attribution, resource drops, and future PvP damage validation.
/// Pass 0 for environmental/hazard damage with no specific source.
///
/// TakeDamage is always called server-side. Implementors should update
/// a NetworkVariable<float> health value and call OnDeath() when it
/// reaches zero. Never call TakeDamage directly from client code —
/// send a ServerRpc instead.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Apply damage to this entity.
    /// </summary>
    /// <param name="amount">Damage amount — always positive.</param>
    /// <param name="sourceId">NetworkObjectId of the damage source. 0 = environmental.</param>
    void TakeDamage(float amount, ulong sourceId);

    /// <summary>
    /// Called by TakeDamage implementors when health reaches zero.
    /// Handle death effects, respawn logic, and resource drops here.
    /// </summary>
    void OnDeath();
}