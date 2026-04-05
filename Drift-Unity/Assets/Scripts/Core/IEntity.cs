/// <summary>
/// IEntity — implemented by all living/active entities in the Drift world.
///
/// Provides a common type identity and faction tag so systems like
/// spawning, targeting, and NPC hiring can classify entities without
/// knowing their concrete type.
///
/// Current implementors: PlayerController, EnemyOrb (stub)
/// Future implementors:  AllyNPC, BossOrb (see ADR-013)
///
/// EntityType and Faction are read-only properties — set once at spawn,
/// never changed at runtime. A Neutral orb does not become Hostile.
/// </summary>
public interface IEntity
{
    /// <summary>
    /// The classification of this entity.
    /// Drives spawn table lookups, stat table selection, and targeting logic.
    /// </summary>
    EntityType EntityType { get; }

    /// <summary>
    /// The faction this entity belongs to.
    /// Used for targeting decisions in PvE and future PvP modes.
    /// </summary>
    Faction Faction { get; }
}

/// <summary>
/// All entity types in Drift. Drives EntityStatTable selection and
/// WorldManager spawn routing.
/// </summary>
public enum EntityType
{
    Player,
    EnemyOrb,
    AllyNPC,
    BossOrb,
    HazardZone,
    Neutral
    // FUTURE: add new entity types here — no other files need changing
}

/// <summary>
/// Faction classification for targeting and NPC behavior.
/// </summary>
public enum Faction
{
    Neutral,   // orbs, stations, shops — no targeting relationship
    Player,    // human-controlled players
    Ally,      // hired NPCs — friendly to Player faction
    Hostile    // enemy orbs, hazard zones — hostile to Player faction
    // FUTURE: PvP mode will add per-team factions here
}