/// <summary>
/// IInteractable — implemented by any world object a player can interact with.
///
/// Current implementors: DepositStation
/// Future implementors:  ZoneShop, Chest, Puzzle, NPC (see ADR-013)
///
/// OnInteract is triggered by single tap when the player is within
/// interaction range of an object tagged as interactable.
/// GetInteractLabel provides the HUD prompt text shown when in range.
///
/// Interaction range checks are done in PlayerController using
/// GameConstants.StationDepositRadius as the default radius.
/// Objects with different ranges should document their override.
/// </summary>
public interface IInteractable
{
    /// <summary>
    /// Called when a player interacts with this object.
    /// </summary>
    /// <param name="playerId">NetworkObjectId of the interacting player.</param>
    void OnInteract(ulong playerId);

    /// <summary>
    /// Returns the action label shown in the HUD when the player is in range.
    /// Example: "Deposit", "Open Shop", "Hire Ally"
    /// </summary>
    string GetInteractLabel();
}