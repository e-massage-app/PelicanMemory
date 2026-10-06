using System;
using System.Collections.Generic;
using PelicanMemory.Core;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Objects;

namespace PelicanMemory.Features.PhoneOrders;

/// <summary>Counts and takes materials from the farm's storage chests, for an order paid from home.</summary>
/// <remarks>
/// Counting happens every frame while a shop menu is drawn, so counts are kept for the rest of the tick and dropped as
/// soon as something is taken. Taking works like the game's own crafting with extra chests: each stack shrinks, empty
/// slots are cleared. A chest the other player has open is left alone.
/// </remarks>
internal class FarmStock
{
    /*********
    ** Fields
    *********/
    private readonly StorageIndex Storage;

    /// <summary>The counts worked out this tick, by qualified item ID.</summary>
    private readonly Dictionary<string, int> Counts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The tick the counts belong to.</summary>
    private int CountsTick = -1;


    /*********
    ** Public methods
    *********/
    public FarmStock(StorageIndex storage)
    {
        this.Storage = storage;
    }

    /// <summary>Count an item in the farm's chests, whatever its quality.</summary>
    public int Count(string itemId)
    {
        string? id = ItemRegistry.QualifyItemId(itemId);
        if (id is null)
            return 0;

        if (this.CountsTick != Game1.ticks)
        {
            this.Counts.Clear();
            this.CountsTick = Game1.ticks;
        }

        if (!this.Counts.TryGetValue(id, out int count))
        {
            foreach (Chest chest in FarmChests.Get())
            {
                foreach (Item? item in chest.GetItemsForPlayer())
                {
                    if (item?.QualifiedItemId == id)
                        count += item.Stack;
                }
            }
            this.Counts[id] = count;
        }

        return count;
    }

    /// <summary>Take up to a number of an item from the farm's chests.</summary>
    /// <returns>How many were actually taken.</returns>
    public int Take(string itemId, int count)
    {
        string? id = ItemRegistry.QualifyItemId(itemId);
        if (id is null || count <= 0)
            return 0;

        int left = count;
        foreach (Chest chest in FarmChests.Get())
        {
            IInventory items = chest.GetItemsForPlayer();
            bool emptied = false;

            for (int i = 0; i < items.Count && left > 0; i++)
            {
                if (items[i] is not Item item || item.QualifiedItemId != id)
                    continue;

                int taken = Math.Min(left, item.Stack);
                items[i] = item.ConsumeStack(taken);
                emptied |= items[i] is null;
                left -= taken;
            }

            if (emptied)
                items.RemoveEmptySlots();
            if (left <= 0)
                break;
        }

        this.Counts.Clear();
        this.Storage.Invalidate();
        return count - left;
    }
}
