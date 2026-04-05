using System;

/// <summary>
/// HireContract — represents a temporarily hired NPC companion.
///
/// Stored as NetworkVariable<HireContract[]> on NetworkPlayer (3 max,
/// see GameConstants.MaxHiredNPCs).
///
/// Contracts are created server-side when a player spends resources at
/// a ZoneShop in a Safe zone. The server tracks remainingDuration and
/// clears the slot when the NPC dies or time runs out (post-submission).
///
/// ownerPlayerId is the NetworkObjectId of the hiring player.
/// AllyNPC.cs reads this field to determine which player to follow.
///
/// This is a session-only contract — no persistence between sessions.
/// See DEVLOG post-submission roadmap for persistent companion plans.
/// </summary>
[Serializable]
public struct HireContract : IEquatable<HireContract>
{
    /// The type of NPC hired — drives EntityStatTable and behavior selection.
    public EntityType npcType;

    /// Resources spent to hire this NPC (recorded for UI display).
    public int cost;

    /// Seconds remaining on this hire contract.
    public float remainingDuration;

    /// NetworkObjectId of the player who hired this NPC.
    public ulong ownerPlayerId;

    /// Convenience constructor.
    public HireContract(EntityType type, int resourceCost,
                        float duration, ulong ownerId)
    {
        npcType = type;
        cost = resourceCost;
        remainingDuration = duration;
        ownerPlayerId = ownerId;
    }

    /// An empty contract slot — no NPC hired.
    public static HireContract Empty =>
        new HireContract(EntityType.Neutral, 0, 0f, 0);

    /// True if this slot has no active hire contract.
    public bool IsEmpty => remainingDuration <= 0f &&
                           ownerPlayerId == 0;

    /// True if this contract still has time remaining.
    public bool IsActive => remainingDuration > 0f;

    public bool Equals(HireContract other) =>
        npcType == other.npcType &&
        cost == other.cost &&
        remainingDuration == other.remainingDuration &&
        ownerPlayerId == other.ownerPlayerId;

    public override bool Equals(object obj) =>
        obj is HireContract other && Equals(other);

    public override int GetHashCode() =>
        System.HashCode.Combine((int)npcType, cost,
                                remainingDuration, ownerPlayerId);
}