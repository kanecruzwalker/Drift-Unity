using UnityEngine;

/// <summary>
/// ShopItemTable — ScriptableObject defining the item pool for ZoneShop.
///
/// One asset total — assigned to ZoneShop. When a player interacts with
/// a Safe zone shop, ZoneShop draws a random selection from this table.
///
/// Full shop UI and purchase flow in feature/hud-and-polish.
/// This table defines the data; ZoneShop consumes it.
///
/// Create asset via:
///   Assets → Create → Drift → Shop Item Table
/// </summary>
[CreateAssetMenu(fileName = "ShopItemTable",
                 menuName = "Drift/Shop Item Table",
                 order = 3)]
public class ShopItemTable : ScriptableObject
{
    [System.Serializable]
    public class ShopItem
    {
        [Tooltip("Display name shown in shop UI.")]
        public string itemName = "Item";

        [Tooltip("Item type — drives inventory slot assignment.")]
        public ItemType itemType = ItemType.Resource;

        [Tooltip("Resource cost to purchase.")]
        public int cost = 10;

        [Tooltip("Effect applied on purchase.")]
        public EffectType effectType = EffectType.SpeedBoost;

        [Tooltip("Effect modifier value (e.g. 1.5 = 50% speed boost).")]
        public float modifier = 1.5f;

        [Tooltip("Effect duration in seconds. -1 = permanent for session.")]
        public float duration = 30f;

        [Tooltip("Icon shown in shop UI — assign in Inspector.")]
        public Sprite icon;
    }

    [Header("Available Items")]
    [Tooltip("All items available in the shop pool. " +
             "ZoneShop randomly selects a subset to offer per visit.")]
    public ShopItem[] items = new ShopItem[]
    {
        new ShopItem
        {
            itemName    = "Speed Boost",
            itemType    = ItemType.Resource,
            cost        = 15,
            effectType  = EffectType.SpeedBoost,
            modifier    = 1.5f,
            duration    = 30f
        },
        new ShopItem
        {
            itemName    = "Resource Multiplier",
            itemType    = ItemType.Resource,
            cost        = 20,
            effectType  = EffectType.ResourceMultiplier,
            modifier    = 2f,
            duration    = 45f
        },
        new ShopItem
        {
            itemName    = "Fog Reveal",
            itemType    = ItemType.Key,
            cost        = 25,
            effectType  = EffectType.FogReveal,
            modifier    = 1f,
            duration    = -1f
        },
        new ShopItem
        {
            itemName    = "Hazard Shield",
            itemType    = ItemType.Resource,
            cost        = 30,
            effectType  = EffectType.HazardShield,
            modifier    = 1f,
            duration    = 20f
        }
    };

    [Header("Shop Settings")]
    [Tooltip("How many items to show per shop visit (randomly selected from pool).")]
    [Range(1, 6)] public int itemsPerVisit = 3;

    /// <summary>
    /// Returns a random selection of items for a shop visit.
    /// Called by ZoneShop.OnInteract() when a player opens the shop.
    /// </summary>
    public ShopItem[] GetRandomSelection()
    {
        int count = Mathf.Min(itemsPerVisit, items.Length);
        ShopItem[] selection = new ShopItem[count];
        int[] indices = new int[items.Length];

        for (int i = 0; i < items.Length; i++)
            indices[i] = i;

        // Fisher-Yates shuffle for fair random selection.
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        for (int i = 0; i < count; i++)
            selection[i] = items[indices[i]];

        return selection;
    }
}