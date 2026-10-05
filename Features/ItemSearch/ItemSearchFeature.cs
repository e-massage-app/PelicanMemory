using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using PelicanMemory.Features.RecipeLookup;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.ItemSearch;

/// <summary>A search tab in the game menu: type part of an item's name, and see how many you have, where, and how to get more.</summary>
/// <remarks>
/// Made for finding things again once everything is put away. Anti-spoil: the search only knows the items the player
/// has come across (<see cref="KnownItems"/>), the shops they have been to, and the recipes they have learned. An item
/// they never met isn't found, even by its exact name.
/// </remarks>
internal class ItemSearchFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The unique tab name.</summary>
    private const string TabName = "JordanNeau.PelicanMemory.Search";

    /// <summary>The magnifying glass in the game's cursor sheet.</summary>
    private static readonly Rectangle IconSource = new(208, 320, 16, 16);

    private readonly KnownItems Known;
    private readonly ItemSearchService Search;


    /*********
    ** Public methods
    *********/
    public override string Id => "item-search";

    public ItemSearchFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, StorageIndex storage, PlayerStore store)
        : base(helper, monitor, harmony, settings)
    {
        ShopCatalog shops = new(monitor);
        this.Known = new KnownItems(store, storage, shops);
        this.Search = new ItemSearchService(this.Known, storage, shops, new RecipeFinder(storage));
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Known.Attach(this.Helper.Events);
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;

        GameMenuTab.AddBeforeOptions(new ModMenuTab(
            Name: TabName,
            GetLabel: () => this.Helper.Translation.Get("search.tab-label"),
            GetIcon: () => (Game1.mouseCursors, IconSource),
            CreatePage: (x, y, width, height) => new ItemSearchPage(x, y, width, height, this.Helper.Translation, this.Search, this.Monitor)
        ));
    }

    protected override void OnDisable()
    {
        this.Known.Detach(this.Helper.Events);
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        GameMenuTab.Remove(TabName);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Release the keyboard when the menu closes, or the search box would keep it (and block the chat).</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.OldMenu is GameMenu menu)
            GameMenuTab.NotifyClosed(menu);
    }
}
