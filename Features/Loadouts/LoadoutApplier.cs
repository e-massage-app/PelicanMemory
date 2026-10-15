using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Objects.Trinkets;

namespace PelicanMemory.Features.Loadouts;

/// <summary>A chest, remembered by where it stands, so an item can go back where it came from.</summary>
/// <param name="Location">The location's unique name.</param>
/// <param name="X">The chest's tile.</param>
/// <param name="Y">The chest's tile.</param>
internal record ChestRef(string Location, int X, int Y)
{
    public static ChestRef Of(Chest chest) => new(chest.Location?.NameOrUniqueName ?? "", (int)chest.TileLocation.X, (int)chest.TileLocation.Y);
}

/// <summary>What applying a preset did.</summary>
internal class LoadoutReport
{
    /// <summary>What the preset wanted but couldn't be found anywhere: name and how many were missing.</summary>
    public List<(string Name, int Missing)> Missing { get; } = new();

    /// <summary>How many items went back to a chest.</summary>
    public int PutAway { get; set; }

    /// <summary>The items which found no chest and stayed in the bag.</summary>
    public List<string> KeptInBag { get; } = new();

    /// <summary>The items outside the preset with no chest of their own (never taken from one, and no chest holds any):
    /// left in the bag for the player to choose where they go.</summary>
    public List<Item> Homeless { get; } = new();

    /// <summary>The items which found room nowhere and were dropped at the player's feet.</summary>
    public List<string> Dropped { get; } = new();
}

/// <summary>Puts on a preset: takes off everything, then takes each wanted item from what the player had and the farm's
/// chests, and puts back the rest where it came from.</summary>
/// <remarks>
/// Order of search for each wanted item: the very same item (fingerprint) before one of the same kind, and what the
/// player already had before the chests. Items taken from a chest remember it (<paramref name="origins"/>), so the next
/// preset puts them back there; otherwise an item goes to a chest which already holds some (like "Put away
/// everywhere"); otherwise it stays in the bag and is listed as having no chest, for the player to choose one (never a
/// chest picked at random: Jordan names his chests). The ground only when the bag is full. Nothing is ever
/// lost: if anything fails half-way, everything still held goes back into the bag or on the ground.
/// </remarks>
internal static class LoadoutApplier
{
    public static LoadoutReport Apply(Farmer player, Loadout loadout, IReadOnlyList<Chest> chests, Dictionary<string, ChestRef> origins)
    {
        LoadoutReport report = new();
        List<Item> held = new();

        try
        {
            // 1. take everything off and out of the bag
            held.AddNonNull(player.Equip<Hat>(null!, player.hat));
            held.AddNonNull(player.Equip<Clothing>(null!, player.shirtItem));
            held.AddNonNull(player.Equip<Clothing>(null!, player.pantsItem));
            held.AddNonNull(player.Equip<Boots>(null!, player.boots));
            held.AddNonNull(player.Equip<Ring>(null!, player.leftRing));
            held.AddNonNull(player.Equip<Ring>(null!, player.rightRing));
            for (int i = 0; i < player.trinketItems.Count; i++)
            {
                held.AddNonNull(player.trinketItems[i]);
                player.trinketItems[i] = null;
            }
            for (int i = 0; i < player.Items.Count; i++)
            {
                held.AddNonNull(player.Items[i]);
                player.Items[i] = null;
            }

            // 2. the equipment
            Item? Take(LoadoutItem? wanted) => wanted is null ? null : TakeItem(wanted, held, chests, origins, report);
            player.Equip(Take(loadout.Equipment.GetValueOrDefault("Hat")) as Hat, player.hat);
            player.Equip(Take(loadout.Equipment.GetValueOrDefault("Shirt")) as Clothing, player.shirtItem);
            player.Equip(Take(loadout.Equipment.GetValueOrDefault("Pants")) as Clothing, player.pantsItem);
            player.Equip(Take(loadout.Equipment.GetValueOrDefault("Boots")) as Boots, player.boots);
            player.Equip(Take(loadout.Equipment.GetValueOrDefault("LeftRing")) as Ring, player.leftRing);
            player.Equip(Take(loadout.Equipment.GetValueOrDefault("RightRing")) as Ring, player.rightRing);
            foreach ((string slot, LoadoutItem? wanted) in loadout.Equipment.Where(pair => pair.Key.StartsWith("Trinket", StringComparison.Ordinal)))
            {
                if (!int.TryParse(slot["Trinket".Length..], out int index) || Take(wanted) is not Trinket trinket)
                    continue;
                if (index >= player.trinketItems.Count && player.stats.Get("trinketSlots") == 0)
                {
                    held.Add(trinket);   // no trinket slot any more: back with the rest
                    continue;
                }
                while (player.trinketItems.Count <= index)
                    player.trinketItems.Add(null);
                player.trinketItems[index] = trinket;
            }

            // 3. the bag, slot by slot
            for (int i = 0; i < loadout.Slots.Count && i < player.Items.Count; i++)
                player.Items[i] = Take(loadout.Slots[i]);

            // 4. what's left goes back where it came from
            PutBack(player, held, chests, origins, report);
        }
        catch
        {
            // never lose anything: whatever is still held goes back to the bag, or on the ground
            foreach (Item item in held)
            {
                try
                {
                    if (AddToBag(player, item) is Item rest)
                        Game1.createItemDebris(rest, player.getStandingPosition(), player.FacingDirection);
                }
                catch
                {
                    // keep going: the other items must still be given back, and the first error is the one to report
                }
            }
            held.Clear();
            throw;
        }

        return report;
    }


    /*********
    ** Private methods
    *********/
    private static void AddNonNull<T>(this List<Item> list, T? item) where T : Item
    {
        if (item != null)
            list.Add(item);
    }

    /// <summary>Put an item back in the bag: on a stack of the very same item, else in an empty slot. Returns what didn't fit.</summary>
    /// <remarks>Not the game's <c>addItemToInventory</c>: that one treats it as a newly received item (on-screen notice,
    /// quests and collections), when it's only the player's own item going back in the bag.</remarks>
    private static Item? AddToBag(Farmer player, Item item)
    {
        Item? rest = item;
        for (int i = 0; i < player.Items.Count && rest != null; i++)
        {
            if (player.Items[i] is Item stack && stack.canStackWith(rest) && Loadout.KeyOf(stack) == Loadout.KeyOf(rest))
            {
                int left = stack.addToStack(rest);
                rest = left > 0 ? rest : null;
                if (rest != null)
                    rest.Stack = left;
            }
        }
        for (int i = 0; i < player.Items.Count && rest != null; i++)
        {
            if (player.Items[i] is null)
            {
                player.Items[i] = rest;
                rest = null;
            }
        }
        return rest;
    }

    /// <summary>Gather the wanted item (and count) from what's held, then from the chests.</summary>
    private static Item? TakeItem(LoadoutItem wanted, List<Item> held, IReadOnlyList<Chest> chests, Dictionary<string, ChestRef> origins, LoadoutReport report)
    {
        Item? result = null;
        int need = Math.Max(1, wanted.Count);

        // the very same first, then the same kind; what the player had first, then the chests
        foreach (int score in new[] { 2, 1 })
        {
            for (int i = 0; i < held.Count && need > 0; i++)
            {
                if (Loadout.Match(wanted, held[i]) != score || !SameKind(result, held[i]))
                    continue;
                Item piece = Split(held[i], need, out bool whole);
                if (whole)
                    held.RemoveAt(i--);
                Merge(ref result, piece, ref need);
            }

            foreach (Chest chest in chests)
            {
                if (need <= 0)
                    break;
                StardewValley.Inventories.IInventory items = chest.GetItemsForPlayer();
                for (int i = 0; i < items.Count && need > 0; i++)
                {
                    if (items[i] is not Item candidate || Loadout.Match(wanted, candidate) != score || !SameKind(result, candidate))
                        continue;
                    Item piece = Split(candidate, need, out bool whole);
                    if (whole)
                        items[i] = null!;
                    origins[Loadout.KeyOf(piece)] = ChestRef.Of(chest);
                    Merge(ref result, piece, ref need);
                }
                items.RemoveEmptySlots();
            }
        }

        if (need > 0)
            report.Missing.Add((wanted.Name, need));
        return result;
    }

    /// <summary>Get whether a candidate can join what's been gathered: one slot holds one stack, so never two qualities
    /// (merging them would change the quality of some items).</summary>
    private static bool SameKind(Item? gathered, Item candidate)
    {
        return gathered is null || Loadout.KeyOf(gathered) == Loadout.KeyOf(candidate);
    }

    /// <summary>Take up to `need` from a stack: the stack itself if it isn't bigger, else a part split off it.</summary>
    private static Item Split(Item stack, int need, out bool whole)
    {
        if (stack.Stack <= need || stack.maximumStackSize() <= 1)
        {
            whole = true;
            return stack;
        }
        whole = false;
        Item piece = stack.getOne();
        piece.Stack = need;
        stack.Stack -= need;
        return piece;
    }

    /// <summary>Add a piece to what's been gathered so far.</summary>
    private static void Merge(ref Item? result, Item piece, ref int need)
    {
        need -= piece.Stack;
        if (result is null)
            result = piece;
        else
            result.Stack += piece.Stack;
    }

    /// <summary>Put back what the preset doesn't use: its own chest, a chest with some of it, the bag, the ground.</summary>
    private static void PutBack(Farmer player, List<Item> held, IReadOnlyList<Chest> chests, Dictionary<string, ChestRef> origins, LoadoutReport report)
    {
        while (held.Count > 0)
        {
            Item item = held[0];
            held.RemoveAt(0);
            string key = Loadout.KeyOf(item);
            Item? rest = item;

            // where it came from
            if (origins.TryGetValue(key, out ChestRef? from) && chests.FirstOrDefault(chest => ChestRef.Of(chest) == from) is Chest home)
            {
                rest = home.addItem(rest);
                if (rest is null)
                {
                    origins.Remove(key);
                    report.PutAway++;
                    continue;
                }
            }

            // a chest which already holds some, the fullest first
            foreach (Chest chest in chests
                .Select(chest => (Chest: chest, Count: chest.GetItemsForPlayer().Where(stored => stored?.QualifiedItemId == item.QualifiedItemId).Sum(stored => stored.Stack)))
                .Where(c => c.Count > 0)
                .OrderByDescending(c => c.Count)
                .Select(c => c.Chest))
            {
                rest = chest.addItem(rest);
                if (rest is null)
                    break;
            }
            if (rest is null)
            {
                report.PutAway++;
                continue;
            }

            // the bag, then the ground
            // no chest of its own: in the bag, for the player to choose (a chest that has some but was full: in the bag too)
            bool hasHome = origins.ContainsKey(key) || chests.Any(chest => chest.GetItemsForPlayer().Any(stored => stored?.QualifiedItemId == item.QualifiedItemId));
            Item? bagged = rest;
            rest = AddToBag(player, rest);
            if (rest is null)
            {
                if (hasHome)
                    report.KeptInBag.Add(item.DisplayName);
                else
                    report.Homeless.Add(bagged);
                continue;
            }
            Game1.createItemDebris(rest, player.getStandingPosition(), player.FacingDirection);
            report.Dropped.Add(item.DisplayName);
        }
    }
}
