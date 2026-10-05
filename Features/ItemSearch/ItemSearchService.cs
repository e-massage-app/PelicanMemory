using System;
using System.Collections.Generic;
using System.Linq;
using PelicanMemory.Core;
using PelicanMemory.Features.RecipeLookup;
using StardewValley;
using StardewValley.ItemTypeDefinitions;

namespace PelicanMemory.Features.ItemSearch;

/// <summary>Everything the player knows about getting hold of one item.</summary>
/// <param name="ItemId">The qualified item ID.</param>
/// <param name="Name">The item's display name.</param>
/// <param name="InBag">How many the player carries.</param>
/// <param name="Stashes">Which containers hold some, biggest pile first.</param>
/// <param name="Offers">The visited shops selling it today.</param>
/// <param name="Recipes">The learned recipes which make it.</param>
internal record ItemDetails(string ItemId, string Name, int InBag, IReadOnlyList<ItemStash> Stashes, IReadOnlyList<ShopOffer> Offers, IReadOnlyList<KnownRecipe> Recipes)
{
    /// <summary>How many are stored away.</summary>
    public int Stored => this.Stashes.Sum(stash => stash.Count);

    /// <summary>Whether nothing at all is known: none owned, no shop, no recipe.</summary>
    public bool IsUnknownSource => this.InBag + this.Stored == 0 && this.Offers.Count == 0 && this.Recipes.Count == 0;
}

/// <summary>Answers the search page: which known items match a name, and where to get each one.</summary>
internal class ItemSearchService
{
    /*********
    ** Fields
    *********/
    private readonly KnownItems Known;
    private readonly StorageIndex Storage;
    private readonly ShopCatalog Shops;
    private readonly RecipeFinder Recipes;


    /*********
    ** Public methods
    *********/
    public ItemSearchService(KnownItems known, StorageIndex storage, ShopCatalog shops, RecipeFinder recipes)
    {
        this.Known = known;
        this.Storage = storage;
        this.Shops = shops;
        this.Recipes = recipes;
    }

    /// <summary>Read everything afresh, since chests and shops may have changed since the page was last open.</summary>
    public void Refresh()
    {
        this.Storage.Invalidate();
        this.Shops.Invalidate();
    }

    /// <summary>Get the names the search may find: one per item the player has come across.</summary>
    public List<SearchEntry> GetEntries()
    {
        List<SearchEntry> entries = new();
        foreach (string id in this.Known.GetAll())
        {
            ParsedItemData? data = ItemRegistry.GetData(id);
            if (data != null && !data.IsErrorItem && !string.IsNullOrWhiteSpace(data.DisplayName))
                entries.Add(new SearchEntry(data.QualifiedItemId, data.DisplayName));
        }
        return entries;
    }

    /// <summary>Count how many of an item the player has, carried or stored, whatever the quality.</summary>
    public int CountOwned(string itemId)
    {
        return CountInBag(itemId) + this.Storage.Count(stored => Matches(stored, itemId));
    }

    /// <summary>Get what the player knows about getting hold of an item.</summary>
    public ItemDetails GetDetails(SearchEntry entry)
    {
        return new ItemDetails(
            ItemId: entry.Id,
            Name: entry.Name,
            InBag: CountInBag(entry.Id),
            Stashes: this.Storage.GetStashes(stored => Matches(stored, entry.Id)),
            Offers: this.Shops.Get(entry.Id),
            Recipes: this.Recipes.FindMaking(entry.Id, KnownItems.GetLearnedRecipes(Game1.player))
        );
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get whether a stack is the item looked for, whatever its quality.</summary>
    private static bool Matches(Item item, string itemId)
    {
        return string.Equals(item.QualifiedItemId, itemId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Count how many of an item the player carries.</summary>
    private static int CountInBag(string itemId)
    {
        return Game1.player.Items.Where(item => item != null && Matches(item, itemId)).Sum(item => item.Stack);
    }
}
