using System;
using System.Collections.Generic;
using System.Linq;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PelicanMemory.Features.ItemSearch;

/// <summary>The items the player has already come across, which are the only ones the search may name.</summary>
/// <remarks>
/// Typing a name must never reveal that an item exists. So the search only knows what the player's own history proves
/// they have met: what they hold or stored, shipped, caught, found, cooked or sewed, what their learned recipes show,
/// what a shop they visited sells today, and — from this version on — everything that ever entered their bag.
/// </remarks>
internal class KnownItems
{
    /*********
    ** Fields
    *********/
    /// <summary>The key under which the items received are remembered, in the player's own save data.</summary>
    private const string ReceivedKey = "items-received";

    private readonly PlayerStore Store;
    private readonly StorageIndex Storage;
    private readonly ShopCatalog Shops;

    /// <summary>The items received since the mod started remembering them, for the current player.</summary>
    private HashSet<string>? Received;


    /*********
    ** Public methods
    *********/
    public KnownItems(PlayerStore store, StorageIndex storage, ShopCatalog shops)
    {
        this.Store = store;
        this.Storage = storage;
        this.Shops = shops;
    }

    /// <summary>Start remembering what enters the player's bag.</summary>
    public void Attach(IModEvents events)
    {
        events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        events.Player.InventoryChanged += this.OnInventoryChanged;
    }

    /// <summary>Stop remembering what enters the player's bag.</summary>
    public void Detach(IModEvents events)
    {
        events.GameLoop.SaveLoaded -= this.OnSaveLoaded;
        events.Player.InventoryChanged -= this.OnInventoryChanged;
        this.Received = null;
    }

    /// <summary>Get the qualified IDs of every item the player has come across.</summary>
    public HashSet<string> GetAll()
    {
        Farmer player = Game1.player;
        HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);

        void AddId(string? id)
        {
            // negative IDs are recipe categories like "any egg", not items
            if (string.IsNullOrWhiteSpace(id) || id.StartsWith('-'))
                return;

            string? qualified = ItemRegistry.QualifyItemId(id);
            if (qualified != null && ItemRegistry.GetData(qualified) != null)
                known.Add(qualified);
        }

        // what the player holds or put away
        foreach (Item? item in player.Items)
            AddId(item?.QualifiedItemId);
        foreach (StoredStack stored in this.Storage.GetAll())
            AddId(stored.Item.QualifiedItemId);

        // what the game's collections already prove they met (these lists use unqualified object IDs)
        foreach (string id in player.basicShipped.Keys.Concat(player.mineralsFound.Keys).Concat(player.archaeologyFound.Keys).Concat(player.recipesCooked.Keys))
            AddId(ItemRegistry.type_object + id);
        foreach (string id in player.fishCaught.Keys.Concat(player.tailoredItems.Keys))
            AddId(id);

        // what the learned recipes show: the crafting and cooking pages display both the result and the ingredients
        foreach (CraftingRecipe recipe in GetLearnedRecipes(player))
        {
            AddId(recipe.GetItemData()?.QualifiedItemId);
            foreach (string ingredient in recipe.recipeList.Keys)
                AddId(ingredient);
        }

        // what a shop they've been to sells today: they could see it on the shelf
        foreach (string id in this.Shops.GetAll().Keys)
            AddId(id);

        // everything that entered their bag since the mod remembers it
        foreach (string id in this.GetReceived())
            AddId(id);

        return known;
    }

    /// <summary>Get every recipe the player has learned, crafting and cooking.</summary>
    public static IEnumerable<CraftingRecipe> GetLearnedRecipes(Farmer player)
    {
        foreach (string name in player.craftingRecipes.Keys)
        {
            if (CraftingRecipe.craftingRecipes.ContainsKey(name))
                yield return new CraftingRecipe(name, isCookingRecipe: false);
        }

        foreach (string name in player.cookingRecipes.Keys)
        {
            if (CraftingRecipe.cookingRecipes.ContainsKey(name))
                yield return new CraftingRecipe(name, isCookingRecipe: true);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Load what the current player received in earlier sessions.</summary>
    private HashSet<string> GetReceived()
    {
        return this.Received ??= new HashSet<string>(this.Store.Read<List<string>>(Game1.player, ReceivedKey) ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Forget the previous save's list, so a different save or player starts from its own.</summary>
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.Received = null;
    }

    /// <summary>Remember any item which enters the bag for the first time.</summary>
    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!e.IsLocalPlayer)
            return;

        HashSet<string> received = this.GetReceived();
        bool changed = false;
        foreach (Item item in e.Added)
            changed |= item?.QualifiedItemId is string id && received.Add(id);

        if (changed)
            this.Store.Write(e.Player, ReceivedKey, received.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList());
    }
}
