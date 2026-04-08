using UnityEngine;

/// <summary>
/// EntityStatTable — ScriptableObject data table for a single EntityType.
///
/// One asset per entity type: Player, EnemyOrb, AllyNPC, BossOrb, etc.
/// WorldManager reads these at spawn time to configure entity stats.
/// PlayerController reads PlayerStatTable for max health and move speed.
///
/// Create assets via:
///   Assets → Create → Drift → Entity Stat Table
///
/// Usage:
///   Assign one asset per EntityType in WorldManager's Inspector.
///   All tuning happens in the Inspector — no code changes needed.
/// </summary>
[CreateAssetMenu(fileName = "EntityStatTable",
                 menuName = "Drift/Entity Stat Table",
                 order = 1)]
public class EntityStatTable : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Which entity type this table applies to.")]
    public EntityType entityType = EntityType.Player;

    [Tooltip("Faction this entity belongs to — drives targeting and NPC behavior.")]
    public Faction faction = Faction.Player;

    [Header("Combat Stats")]
    [Tooltip("Maximum health points. Players start at this value.")]
    public float maxHealth = 100f;

    [Tooltip("Damage this entity deals on contact (PvE/PvP modes).")]
    public float contactDamage = 10f;

    [Header("Movement Stats")]
    [Tooltip("Base movement speed multiplier applied to physics forces.")]
    public float moveSpeed = 1f;

    [Tooltip("Linear drag applied to this entity's Rigidbody. Higher = more resistance.")]
    public float linearDrag = 2f;

    [Header("Collection Stats (Player only)")]
    [Tooltip("Radius multiplier for area collect pulse. Scales GameConstants.HoldPulseRadius.")]
    public float collectRadiusMultiplier = 1f;

    [Tooltip("Resource multiplier — SpeedBoost effect modifies this at runtime.")]
    public float resourceMultiplier = 1f;

    [Header("Spawn Settings (Enemy/NPC only)")]
    [Tooltip("Score/resource value awarded when this entity is defeated.")]
    public int defeatValue = 5;

    [Tooltip("Patrol speed for EnemyOrb waypoint movement.")]
    public float patrolSpeed = 2f;

    [Tooltip("Detection radius — enemy starts pursuing player within this range (PvE).")]
    public float detectionRadius = 5f;
}