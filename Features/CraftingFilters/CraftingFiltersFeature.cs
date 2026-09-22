using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.Features.CraftingFilters;

/// <summary>Adds side tabs to the crafting page, to show one family of recipes at a time.</summary>
/// <remarks>
/// The crafting page lists every recipe in one long grid, in no particular order. This only hides part of that list:
/// the recipes themselves, known and unknown, are exactly the ones the game would have shown.
/// </remarks>
internal class CraftingFiltersFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static CraftingFiltersFeature? Instance;

    /// <summary>How far the selected tab sticks out, matching the collections page.</summary>
    private const int ActiveTabOffset = 8;

    /// <summary>The family currently shown, which is remembered while the menu stays open.</summary>
    private string Current = CraftingCategories.Ids[0];


    /*********
    ** Public methods
    *********/
    public override string Id => "crafting-filters";

    public CraftingFiltersFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Postfix(
            AccessTools.Method(typeof(CraftingPage), "GetRecipesToDisplay"),
            typeof(CraftingFiltersFeature),
            nameof(After_GetRecipesToDisplay)
        );

        this.Postfix(
            AccessTools.Method(typeof(CraftingPage), nameof(CraftingPage.draw), new[] { typeof(SpriteBatch) }),
            typeof(CraftingFiltersFeature),
            nameof(After_Draw)
        );

        this.Prefix(
            AccessTools.Method(typeof(CraftingPage), nameof(CraftingPage.receiveLeftClick)),
            typeof(CraftingFiltersFeature),
            nameof(Before_ReceiveLeftClick)
        );
    }

    protected override void OnDisable()
    {
        this.Current = CraftingCategories.Ids[0];
        CraftingCategories.Reset();
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Keep only the recipes of the chosen family.</summary>
    private static void After_GetRecipesToDisplay(CraftingPage __instance, ref List<string> __result)
    {
        if (Instance is null || __instance.cooking || Instance.Current == CraftingCategories.Ids[0])
            return;

        try
        {
            List<string> filtered = __result.Where(name => CraftingCategories.Classify(name) == Instance.Current).ToList();
            if (filtered.Count > 0)
                __result = filtered;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to filter the crafting recipes:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw the tabs down the left edge of the page.</summary>
    private static void After_Draw(CraftingPage __instance, SpriteBatch b)
    {
        if (Instance is null || __instance.cooking)
            return;

        try
        {
            string? hovered = null;
            foreach ((string id, Rectangle slot) in GetTabs(__instance))
            {
                DrawTab(b, slot, CraftingCategories.GetIcon(id), Instance.Current == id);
                if (slot.Contains(Game1.getOldMouseX(), Game1.getOldMouseY()))
                    hovered = id;
            }

            // drawn last so it sits over the tabs, like every other tooltip in the menu
            if (hovered != null)
            {
                IClickableMenu.drawHoverText(b, Instance.Helper.Translation.Get($"crafting-filters.{hovered}"), Game1.smallFont);
                __instance.drawMouse(b);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the crafting filters:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Switch family when a tab is clicked.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_ReceiveLeftClick(CraftingPage __instance, int x, int y)
    {
        if (Instance is null || __instance.cooking)
            return true;

        try
        {
            foreach ((string id, Rectangle slot) in GetTabs(__instance))
            {
                if (!slot.Contains(x, y))
                    continue;

                if (id != Instance.Current)
                    Instance.Show(__instance, id);
                return false;
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to switch the crafting filter:\n{ex}", LogLevel.Error);
        }

        return true;
    }

    /// <summary>Show one family of recipes, or say nothing can be crafted in it.</summary>
    private void Show(CraftingPage page, string id)
    {
        string previous = this.Current;
        this.Current = id;

        List<string> recipes = this.Helper.Reflection.GetMethod(page, "GetRecipesToDisplay").Invoke<List<string>>();
        if (recipes.Count == 0 || (id != CraftingCategories.Ids[0] && recipes.All(name => CraftingCategories.Classify(name) != id)))
        {
            // nothing here yet: stay where we were rather than showing the whole list again
            this.Current = previous;
            Game1.playSound("cancel");
            return;
        }

        page.pagesOfCraftingRecipes.Clear();
        page.currentPageClickableComponents.Clear();
        this.Helper.Reflection.GetMethod(page, "layoutRecipes").Invoke(recipes);
        page.currentCraftingPage = 0;

        Game1.playSound("smallSelect");
    }

    /// <summary>Get the tabs down the left edge, in the order the families are listed.</summary>
    private static IEnumerable<(string Id, Rectangle Slot)> GetTabs(CraftingPage page)
    {
        for (int i = 0; i < CraftingCategories.Ids.Length; i++)
        {
            string id = CraftingCategories.Ids[i];
            bool active = Instance?.Current == id;

            // the same column as the collections page's own tabs, so it reads as part of the menu
            yield return (id, new Rectangle(page.xPositionOnScreen - 64 + (active ? ActiveTabOffset : 0), page.yPositionOnScreen + 64 * (1 + i), 64, 64));
        }
    }

    /// <summary>Draw one tab: a vanilla frame with the family's item sprite inside it.</summary>
    private static void DrawTab(SpriteBatch b, Rectangle slot, string itemId, bool active)
    {
        IClickableMenu.drawTextureBox(b, slot.X, slot.Y, slot.Width, slot.Height, active ? Color.White : Color.White * 0.8f);

        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(itemId);
        Rectangle source = data.GetSourceRect();
        if (source.Width <= 0 || source.Height <= 0)
            return;

        float scale = 36f / Math.Max(source.Width, source.Height);
        Vector2 position = new(
            slot.X + (slot.Width - source.Width * scale) / 2f,
            slot.Y + (slot.Height - source.Height * scale) / 2f
        );

        b.Draw(data.GetTexture(), position, source, active ? Color.White : Color.White * 0.75f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.88f);
    }
}
