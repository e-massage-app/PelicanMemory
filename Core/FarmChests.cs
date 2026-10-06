using System.Collections.Generic;
using StardewValley.Objects;

namespace PelicanMemory.Core;

/// <summary>The player's storage chests on the farm, as the features which reach into them see them.</summary>
/// <remarks>Shared so that putting away, crafting and buying by phone all agree on which chests count.</remarks>
internal static class FarmChests
{
    /// <summary>Get the storage chests of the farm and its buildings, fridges included.</summary>
    /// <param name="skipOpenedByOthers">Whether to leave out the chests another player has open at this moment.</param>
    /// <remarks>Junimo chests all share one inventory, so only one of them is returned.</remarks>
    public static List<Chest> Get(bool skipOpenedByOthers = true)
    {
        List<Chest> chests = new();
        HashSet<object> inventories = new(ReferenceEqualityComparer.Instance);

        StorageIndex.ForEachContainer((chest, location) =>
        {
            if (!FarmPlaces.IsOnFarm(location) || !IsStorage(chest))
                return;

            // opened by the other player at this very moment: leave it to them
            if (skipOpenedByOthers && chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld())
                return;

            if (inventories.Add(chest.GetItemsForPlayer()))
                chests.Add(chest);
        });

        return chests;
    }

    /// <summary>Get whether a chest is meant for storage, rather than a shipping bin, hopper or other machine-like chest.</summary>
    public static bool IsStorage(Chest chest)
    {
        return chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest or Chest.SpecialChestTypes.JunimoChest;
    }
}
