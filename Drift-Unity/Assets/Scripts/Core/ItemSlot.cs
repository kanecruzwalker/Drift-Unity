using System;

/// <summary>
/// ItemSlot — represents a single slot in a player's inventory.
///
/// Stored as NetworkVariable<ItemSlot[]> on NetworkPlayer (6 slots max,
/// see GameConstants.InventorySlotCount).
///
/// INetworkSerializable is required for NetworkVariable serialization.
/// isEquipped drives PlayerController stat modifiers and UIFeedback
/// slot highlight. Items are manually equipped/unequipped by the player
/// via the inventory UI (post-submission).
///
/// An empty slot has itemType = ItemType.None and isEquipped = false.
/// </summary>
[Serializable]
public struct ItemSlot : IEquatable<ItemSlot>
{
    /// The type of item occupying this slot. None = empty.
    public ItemType itemType;

    /// Whether this item is currently equipped and applying its effect.
    public bool isEquipped;

    /// Convenience constructor for creating a populated slot.
    public ItemSlot(ItemType type, bool equipped = false)
    {
        itemType = type;
        isEquipped = equipped;
    }

    /// An empty slot with no item.
    public static ItemSlot Empty => new ItemSlot(ItemType.None, false);

    /// True if this slot has no item.
    public bool IsEmpty => itemType == ItemType.None;

    public bool Equals(ItemSlot other) =>
        itemType == other.itemType && isEquipped == other.isEquipped;

    public override bool Equals(object obj) =>
        obj is ItemSlot other && Equals(other);

    public override int GetHashCode() =>
        System.HashCode.Combine((int)itemType, isEquipped);
}

/// <summary>
/// All item types that can occupy an inventory slot.
/// Drives ShopItemTable lookups and PlayerController effect application.
/// </summary>
public enum ItemType
{
    None,           // empty slot
    Resource,       // raw collected resource
    Weapon,         // orbiting weapon — damages enemies on contact
    Key,            // unlocks a locked zone or chest
    PuzzlePiece     // component of a zone puzzle
    // FUTURE: Consumable — single-use item with immediate effect
    // FUTURE: Upgrade    — permanent stat improvement purchased at shop
}