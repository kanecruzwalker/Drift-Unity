using UnityEngine;

/// <summary>
/// SpawnRateTable — ScriptableObject defining spawn rates per ZoneState.
///
/// One asset total — assigned to WorldManager. WorldManager's SpawnTick()
/// reads the rates for the current zone's ZoneState each tick.
///
/// Rates are per-second spawn probabilities (0-1).
/// A rate of 0.1 means roughly 1 spawn every 10 seconds per zone.
///
/// Create asset via:
///   Assets → Create → Drift → Spawn Rate Table
/// </summary>
[CreateAssetMenu(fileName = "SpawnRateTable",
                 menuName = "Drift/Spawn Rate Table",
                 order = 2)]
public class SpawnRateTable : ScriptableObject
{
    [Header("Orb Spawn Rates (per second, per zone)")]
    [Tooltip("Orb spawn rate in Undiscovered zones — high yield, high risk.")]
    [Range(0f, 1f)] public float orbRateUndiscovered = 0.3f;

    [Tooltip("Orb spawn rate in Discovered zones — still high yield.")]
    [Range(0f, 1f)] public float orbRateDiscovered = 0.25f;

    [Tooltip("Orb spawn rate in Contested zones — medium yield.")]
    [Range(0f, 1f)] public float orbRateContested = 0.15f;

    [Tooltip("Orb spawn rate in Safe zones — low yield, peaceful.")]
    [Range(0f, 1f)] public float orbRateSafe = 0.05f;

    [Header("Enemy Spawn Rates (PvE only, per second, per zone)")]
    [Tooltip("Enemy spawn rate in Undiscovered zones — high danger.")]
    [Range(0f, 1f)] public float enemyRateUndiscovered = 0.2f;

    [Tooltip("Enemy spawn rate in Discovered zones — moderate danger.")]
    [Range(0f, 1f)] public float enemyRateDiscovered = 0.12f;

    [Tooltip("Enemy spawn rate in Contested zones — enemies present.")]
    [Range(0f, 1f)] public float enemyRateContested = 0.08f;

    [Tooltip("Enemy spawn rate in Safe zones — zero, no enemies in safe areas.")]
    [Range(0f, 1f)] public float enemyRateSafe = 0f;

    [Header("Max Active Counts (per zone)")]
    [Tooltip("Maximum orbs active at once per zone.")]
    public int maxOrbsPerZone = 8;

    [Tooltip("Maximum enemies active at once per zone (PvE).")]
    public int maxEnemiesPerZone = 3;

    /// <summary>
    /// Returns the orb spawn rate for the given zone state.
    /// Called by WorldManager.SpawnTick() each physics frame.
    /// </summary>
    public float GetOrbRate(ZoneState state) => state switch
    {
        ZoneState.Undiscovered => orbRateUndiscovered,
        ZoneState.Discovered => orbRateDiscovered,
        ZoneState.Contested => orbRateContested,
        ZoneState.Safe => orbRateSafe,
        _ => 0f
    };

    /// <summary>
    /// Returns the enemy spawn rate for the given zone state.
    /// Returns 0 in Passive game mode — caller must check GameMode.
    /// </summary>
    public float GetEnemyRate(ZoneState state) => state switch
    {
        ZoneState.Undiscovered => enemyRateUndiscovered,
        ZoneState.Discovered => enemyRateDiscovered,
        ZoneState.Contested => enemyRateContested,
        ZoneState.Safe => enemyRateSafe,
        _ => 0f
    };
}