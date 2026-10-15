using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Enchantments;
using StardewValley.Objects;
using StardewValley.Objects.Trinkets;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.Loadouts;

/// <summary>One item of a preset: what it is, how many, and its fingerprint to find the very same one again.</summary>
/// <param name="Id">The qualified item ID.</param>
/// <param name="Count">How many the preset wants.</param>
/// <param name="Key">The fingerprint (<see cref="Loadout.KeyOf"/>): an enchanted weapon or a combined ring is found again as it was.</param>
/// <param name="Name">The name shown when it's missing.</param>
internal record LoadoutItem(string Id, int Count, string Key, string Name);

/// <summary>A saved outfit: the equipment and every slot of the bag.</summary>
/// <param name="Name">The name the player gave it.</param>
/// <param name="Equipment">The worn items, by slot: Hat, Shirt, Pants, Boots, LeftRing, RightRing, Trinket0…</param>
/// <param name="Slots">The bag, slot by slot (<c>null</c> = empty).</param>
internal record Loadout(string Name, Dictionary<string, LoadoutItem?> Equipment, List<LoadoutItem?> Slots)
{
    /// <summary>The equipment slots, in the order they're filled.</summary>
    public static readonly string[] EquipmentSlots = { "Hat", "Shirt", "Pants", "Boots", "LeftRing", "RightRing" };

    /// <summary>Describe an item of the bag or equipment for a preset.</summary>
    public static LoadoutItem? Describe(Item? item)
    {
        return item is null ? null : new LoadoutItem(item.QualifiedItemId, item.Stack, KeyOf(item), item.DisplayName);
    }

    /// <summary>Take a snapshot of what a player wears and carries.</summary>
    public static Loadout Capture(Farmer player, string name)
    {
        Dictionary<string, LoadoutItem?> equipment = new()
        {
            ["Hat"] = Describe(player.hat.Value),
            ["Shirt"] = Describe(player.shirtItem.Value),
            ["Pants"] = Describe(player.pantsItem.Value),
            ["Boots"] = Describe(player.boots.Value),
            ["LeftRing"] = Describe(player.leftRing.Value),
            ["RightRing"] = Describe(player.rightRing.Value)
        };
        for (int i = 0; i < player.trinketItems.Count; i++)
            equipment["Trinket" + i] = Describe(player.trinketItems[i]);

        return new Loadout(name, equipment, player.Items.Select(Describe).ToList());
    }

    /// <summary>A fingerprint telling apart two items with the same ID: a weapon's forges and enchantments, a tool's
    /// upgrade and enchantments, a combined ring's rings, a trinket's rolled stats, an object's quality.</summary>
    public static string KeyOf(Item item)
    {
        switch (item)
        {
            case CombinedRing combined:
                return "CR:" + string.Join("+", combined.combinedRings.Select(ring => ring is null ? "?" : KeyOf(ring)).OrderBy(key => key));
            case Trinket trinket:
                return $"{item.QualifiedItemId}#{trinket.generationSeed.Value}";
            case Tool tool:
                string enchantments = string.Join(",", tool.enchantments.Select(e => $"{e.GetType().Name}:{e.GetLevel()}").OrderBy(e => e));
                return $"{item.QualifiedItemId}#{tool.UpgradeLevel}#{enchantments}";
            case SObject obj:
                return $"{item.QualifiedItemId}#{obj.Quality}";
            default:
                return item.QualifiedItemId;
        }
    }

    /// <summary>How well a candidate matches what a preset wants: 2 the very same, 1 the same kind, 0 not at all.</summary>
    public static int Match(LoadoutItem wanted, Item candidate)
    {
        // a combined ring is only ever itself: two combined rings share one item ID
        if (wanted.Key.StartsWith("CR:", StringComparison.Ordinal) || candidate is CombinedRing)
            return KeyOf(candidate) == wanted.Key ? 2 : 0;
        if (candidate.QualifiedItemId != wanted.Id)
            return 0;
        return KeyOf(candidate) == wanted.Key ? 2 : 1;
    }
}
