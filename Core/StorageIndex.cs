using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace PelicanMemory.Core;

/// <summary>Where some of an item is stored.</summary>
/// <param name="Place">The place's display name.</param>
/// <param name="Count">How many are there.</param>
/// <param name="ChestId">The container's item ID, for its sprite.</param>
/// <param name="ChestColor">The colour the player painted the chest, or <c>null</c> for a plain one.</param>
/// <param name="Label">The name the player gave the chest, or <c>null</c> if they never named it.</param>
internal record ItemStash(string Place, int Count, string ChestId, Color? ChestColor, string? Label = null)
{
    /// <summary>How to refer to the container: its name if it has one, otherwise where it stands.</summary>
    public string DisplayName => this.Label ?? this.Place;
}

/// <summary>A stack sitting in one of the player's containers.</summary>
internal record StoredStack(Item Item, Chest Container, string Place, string ChestId, Color? ChestColor);

/// <summary>Remembers what's in the player's own chests, so features can answer "where did I put this?".</summary>
/// <remarks>
/// Walking every location is far too slow to do while a tooltip is drawn, so the contents are scanned once and kept
/// until something could have changed: a chest was opened, or a new day started.
/// </remarks>
internal class StorageIndex
{
    /*********
    ** Fields
    *********/
    /// <summary>Every stack found on the last scan, or <c>null</c> if it must be scanned again.</summary>
    private List<StoredStack>? Cache;


    /*********
    ** Public methods
    *********/
    public StorageIndex(IModHelper helper)
    {
        helper.Events.GameLoop.SaveLoaded += (_, _) => this.Invalidate();
        helper.Events.GameLoop.DayStarted += (_, _) => this.Invalidate();
        helper.Events.Display.MenuChanged += this.OnMenuChanged;
    }

    /// <summary>Forget what was found, so the next question triggers a fresh scan.</summary>
    public void Invalidate()
    {
        this.Cache = null;
    }

    /// <summary>Get every stack stored in the player's containers.</summary>
    public IReadOnlyList<StoredStack> GetAll()
    {
        return this.Cache ??= Scan();
    }

    /// <summary>Get which containers hold a matching item, biggest pile first.</summary>
    /// <remarks>Grouped per container rather than per place, since players tell their chests apart by colour.</remarks>
    /// <param name="match">Which items to count.</param>
    /// <param name="exclude">The contents of a container to leave out, such as the one the player is looking into.</param>
    public IReadOnlyList<ItemStash> GetStashes(Func<Item, bool> match, IList<Item>? exclude = null)
    {
        Dictionary<StorageKey, int> byContainer = new();

        foreach (StoredStack entry in this.GetAll())
        {
            if (!match(entry.Item) || IsExcluded(entry, exclude))
                continue;

            StorageKey key = new(entry.Place, entry.ChestId, entry.ChestColor, ChestLabels.Get(entry.Container));
            byContainer.TryGetValue(key, out int count);
            byContainer[key] = count + entry.Item.Stack;
        }

        return byContainer
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new ItemStash(pair.Key.Place, pair.Value, pair.Key.ChestId, pair.Key.Color, pair.Key.Label))
            .ToList();
    }

    /// <summary>Count how many matching items are stored away.</summary>
    /// <param name="match">Which items to count.</param>
    /// <param name="exclude">The contents of a container to leave out, such as the one the player is looking into.</param>
    public int Count(Func<Item, bool> match, IList<Item>? exclude = null)
    {
        int count = 0;
        foreach (StoredStack entry in this.GetAll())
        {
            if (match(entry.Item) && !IsExcluded(entry, exclude))
                count += entry.Item.Stack;
        }
        return count;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>What makes one container different from another in a list.</summary>
    private record StorageKey(string Place, string ChestId, Color? Color, string? Label);

    /// <summary>Get whether a stack sits in the container being left out.</summary>
    /// <remarks>Compared by the contents themselves, so an open chest and an open fridge are both recognised.</remarks>
    private static bool IsExcluded(StoredStack entry, IList<Item>? exclude)
    {
        return exclude != null && ReferenceEquals(entry.Container.Items, exclude);
    }

    /// <summary>Rescan once the player closes a container, since they probably moved something.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.OldMenu is StardewValley.Menus.ItemGrabMenu)
            this.Invalidate();
    }

    /// <summary>Collect every item in the player's own chests and fridges, with the container it's in.</summary>
    private static List<StoredStack> Scan()
    {
        List<StoredStack> stored = new();
        if (!Context.IsWorldReady)
            return stored;

        Utility.ForEachLocation(location =>
        {
            string place = GetPlaceName(location);

            foreach (SObject obj in location.Objects.Values)
            {
                if (obj is Chest chest && chest.playerChest.Value)
                    Add(chest, place);
            }

            // the kitchen fridge counts too: it's where cooking ingredients usually live
            switch (location)
            {
                case FarmHouse { fridge.Value: not null } farmHouse:
                    Add(farmHouse.fridge.Value, place);
                    break;

                case IslandFarmHouse { fridge.Value: not null } islandHouse:
                    Add(islandHouse.fridge.Value, place);
                    break;
            }

            return true;
        });

        return stored;

        void Add(Chest chest, string place)
        {
            Color? color = chest.playerChoiceColor.Value == Color.Black ? null : chest.playerChoiceColor.Value;

            foreach (Item item in chest.Items)
            {
                if (item != null)
                    stored.Add(new StoredStack(item, chest, place, chest.QualifiedItemId, color));
            }
        }
    }

    /// <summary>Get a readable name for a place.</summary>
    private static string GetPlaceName(GameLocation location)
    {
        string? name = location.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? location.Name : name;
    }
}
