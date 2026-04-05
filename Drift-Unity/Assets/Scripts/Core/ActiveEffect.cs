using System;

/// <summary>
/// ActiveEffect — a timed buff or debuff currently applied to a player.
///
/// Stored as NetworkVariable<ActiveEffect[]> on NetworkPlayer (4 max,
/// see GameConstants.MaxActiveEffects).
///
/// Effects are applied server-side when purchased at a ZoneShop.
/// The server ticks remainingTime down each frame and removes the effect
/// when it reaches zero (post-submission — tick logic lives in GameManager).
///
/// modifier meaning varies by EffectType:
///   SpeedBoost / SpeedDebuff    → multiplier on PlayerMaxSpeed (e.g. 1.5 = +50%)
///   ResourceMultiplier          → multiplier on resources gained per collect
///   FogReveal                   → radius in world units added to fog reveal range
///   HazardShield                → damage reduction fraction (0-1, e.g. 0.5 = 50% less)
/// </summary>
[Serializable]
public struct ActiveEffect : IEquatable<ActiveEffect>
{
    /// The type of effect being applied.
    public EffectType effectType;

    /// Numeric modifier — interpretation depends on effectType (see summary).
    public float modifier;

    /// Seconds remaining before this effect expires.
    public float remainingTime;

    /// Convenience constructor.
    public ActiveEffect(EffectType type, float mod, float duration)
    {
        effectType = type;
        modifier = mod;
        remainingTime = duration;
    }

    /// An inactive/empty effect slot.
    public static ActiveEffect None => new ActiveEffect(EffectType.None, 0f, 0f);

    /// True if this effect slot is empty.
    public bool IsEmpty => effectType == EffectType.None;

    /// True if this effect still has time remaining.
    public bool IsActive => remainingTime > 0f;

    public bool Equals(ActiveEffect other) =>
        effectType == other.effectType &&
        modifier == other.modifier &&
        remainingTime == other.remainingTime;

    public override bool Equals(object obj) =>
        obj is ActiveEffect other && Equals(other);

    public override int GetHashCode() =>
        System.HashCode.Combine((int)effectType, modifier, remainingTime);
}

/// <summary>
/// All buff and debuff effect types available from ZoneShop purchases.
/// Modifier interpretation documented on ActiveEffect.
/// </summary>
public enum EffectType
{
    None,               // empty slot — no effect
    SpeedBoost,         // increases PlayerMaxSpeed by modifier multiplier
    SpeedDebuff,        // decreases PlayerMaxSpeed by modifier multiplier
    ResourceMultiplier, // multiplies resources gained per collect
    FogReveal,          // expands local fog-of-war reveal radius
    HazardShield        // reduces damage taken from hazard zones
    // FUTURE: add new effect types here — ShopItemTable drives their values
}