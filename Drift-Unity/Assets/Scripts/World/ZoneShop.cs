using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ZoneShop — interactable shop that appears on Safe zone stations.
///
/// STUB — IInteractable implemented, shop UI and purchase flow in feature/hud-and-polish.
///
/// Attaches to the DepositStation prefab alongside the station component.
/// Only active when the station's zone is ZoneState.Safe.
/// OnInteract() will open ShopUI and pass a random ShopItemTable selection.
/// </summary>
public class ZoneShop : NetworkBehaviour, IInteractable
{
    [Header("Data")]
    [Tooltip("Shop item pool — assign ShopItemTable asset in Inspector.")]
    [SerializeField] private ShopItemTable shopItemTable;

    /// Whether the shop is currently open for interaction.
    public bool IsOpen { get; private set; } = false;

    /// <summary>
    /// Opens the shop for this player.
    /// FUTURE: send ShopItemTable.GetRandomSelection() to ShopUI.
    /// </summary>
    public void OnInteract(ulong playerId)
    {
        if (!IsOpen) return;
        Debug.Log($"[ZoneShop] Player {playerId} opened shop.");
        // FUTURE: ShopUI.Instance?.Open(shopItemTable.GetRandomSelection(), playerId);
    }

    public string GetInteractLabel() => IsOpen ? "Shop" : "";

    /// Called by DepositStation when zone becomes Safe.
    public void SetOpen(bool open)
    {
        IsOpen = open;
        Debug.Log($"[ZoneShop] Shop {(open ? "opened" : "closed")}.");
    }
}