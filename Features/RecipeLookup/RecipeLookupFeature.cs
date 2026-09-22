using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.RecipeLookup;

/// <summary>Hover an item, press a key, and see which of your known recipes use it — and where the ingredients are.</summary>
/// <remarks>
/// Only recipes the player has learned are listed, and the only stock shown is their own bag and chests. The window
/// opens on demand, so nothing clutters the tooltip: it only gains one line saying the key is available.
/// </remarks>
internal class RecipeLookupFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static RecipeLookupFeature? Instance;

    /// <summary>The config key for the keybind.</summary>
    private const string KeybindKey = "recipe-lookup";

    /// <summary>The collections cooking icon, used as the tooltip stamp.</summary>
    private static readonly Rectangle RecipeIcon = new(688, 64, 16, 16);

    /// <summary>The colour of the tooltip line, chosen to read as ours rather than as part of the item description.</summary>
    private static readonly Color HintColor = new(40, 100, 60);

    private readonly RecipeFinder Finder;

    /// <summary>How many known recipes use an item, cached while the menus stay open.</summary>
    private readonly Dictionary<string, int> RecipeCountCache = new();


    /*********
    ** Public methods
    *********/
    public override string Id => "recipe-lookup";

    public RecipeLookupFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, StorageIndex storage)
        : base(helper, monitor, harmony, settings)
    {
        this.Finder = new RecipeFinder(storage);
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        TooltipBadges.AddProvider(this.GetTooltipBadge);

    }

    protected override void OnDisable()
    {
        this.Helper.Events.Input.ButtonsChanged -= this.OnButtonsChanged;
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        TooltipBadges.RemoveProvider(this.GetTooltipBadge);

        this.RecipeCountCache.Clear();
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Stamp the tooltip with a cooking icon and the key to press, so it stands out from the description.</summary>
    private TooltipBadge? GetTooltipBadge(Item item)
    {
        if (!Context.IsWorldReady)
            return null;

        int count = this.CountRecipes(item);
        if (count <= 0)
            return null;

        return new TooltipBadge(
            Game1.mouseCursors,
            RecipeIcon,
            Label: this.Settings.GetKeybind(KeybindKey, "R").ToString(),
            Text: this.Helper.Translation.Get("recipes.tooltip", new { count }),
            TextColor: HintColor
        );
    }

    /// <summary>Open the recipe window for the item under the cursor.</summary>
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !this.Settings.GetKeybind(KeybindKey, "R").JustPressed())
            return;

        try
        {
            if (HoveredItem.Get(this.Helper) is not Item item)
                return;

            this.Helper.Input.SuppressActiveKeybinds(this.Settings.GetKeybind(KeybindKey, "R"));

            IReadOnlyList<KnownRecipe> recipes = this.Finder.Find(item);
            Game1.activeClickableMenu = new RecipeListMenu(this.Helper.Translation, item, recipes);
            Game1.playSound("bigSelect");
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to open the recipe window:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Count the known recipes using an item, cached since tooltips ask on every frame.</summary>
    private int CountRecipes(Item item)
    {
        if (this.RecipeCountCache.TryGetValue(item.QualifiedItemId, out int cached))
            return cached;

        int count = this.Finder.Count(item);
        this.RecipeCountCache[item.QualifiedItemId] = count;
        return count;
    }

    /// <summary>Forget the cached counts, since learning a recipe changes them.</summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.RecipeCountCache.Clear();
    }
}
