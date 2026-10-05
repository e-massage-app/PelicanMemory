using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>A tab the mod adds to the game menu.</summary>
/// <param name="Name">The unique tab name.</param>
/// <param name="GetLabel">Get the text shown when the tab is hovered.</param>
/// <param name="GetIcon">Get the sprite drawn on the tab.</param>
/// <param name="CreatePage">Create the page shown when the tab is selected, from the game menu's bounds.</param>
internal record ModMenuTab(string Name, Func<string> GetLabel, Func<(Texture2D Texture, Rectangle Source)> GetIcon, Func<int, int, int, int, IClickableMenu> CreatePage);

/// <summary>A page which wants to know when it's shown or left, and may take the keyboard while it's shown.</summary>
internal interface IModMenuPage
{
    /// <summary>Whether the page is taking typed letters, so the game's keys must leave them alone.</summary>
    bool CapturesKeyboard { get; }

    /// <summary>The page became the one on screen.</summary>
    void OnShown();

    /// <summary>The page was left, by changing tab or closing the menu.</summary>
    void OnHidden();
}

/// <summary>Injects the mod's tabs into the vanilla <see cref="GameMenu"/>.</summary>
/// <remarks>
/// The vanilla menu hardcodes its tabs in several places, so we patch each one:
/// <list type="bullet">
///   <item>the constructor, to add our pages and tabs (done in the constructor so that <c>new GameMenu(ourTab)</c>, used by the game on window resize, works);</item>
///   <item><see cref="GameMenu.getTabNumberFromName"/>, which returns -1 for unknown names;</item>
///   <item>the tab icon drawing, which ignores unknown names. We draw our icons right after the menu box, before vanilla tabs and page content, so the layering matches vanilla tabs;</item>
///   <item><see cref="GameMenu.changeTab"/>, to tell our pages when they're shown or left;</item>
///   <item><see cref="GameMenu.receiveKeyPress"/>, which closes the menu on E even while the player types in one of our pages.</item>
/// </list>
/// Two tabs fit after the vanilla ones: a third would sit under the menu's close button.
/// </remarks>
internal static class GameMenuTab
{
    /// <summary>The name of the options tab.</summary>
    public const string TabName = "JordanNeau.PelicanMemory";

    /// <summary>The controller navigation ID for our first tab (the vanilla ones end at <see cref="GameMenu.region_exitTab"/>).</summary>
    private const int FirstTabComponentId = GameMenu.region_exitTab + 1;

    /// <summary>The most tabs which fit before the close button.</summary>
    private const int MaxTabs = 2;

    /// <summary>The item whose sprite is drawn on the options tab (Lost Book).</summary>
    private const string OptionsIconItemId = "(O)102";

    private static IMonitor Monitor = null!;

    /// <summary>The tabs to add, in order.</summary>
    private static readonly List<ModMenuTab> Tabs = new();

    /// <summary>The menu whose tab icons must be drawn on the next dialogue box draw.</summary>
    private static GameMenu? PendingIconDraw;

    public static void Apply(Harmony harmony, IMonitor monitor, FeatureRegistry registry)
    {
        Monitor = monitor;

        Tabs.Add(new ModMenuTab(
            Name: TabName,
            GetLabel: () => registry.Translate("menu.tab-label"),
            GetIcon: () =>
            {
                ParsedItemData icon = ItemRegistry.GetDataOrErrorItem(OptionsIconItemId);
                return (icon.GetTexture(), icon.GetSourceRect());
            },
            CreatePage: (x, y, width, height) => new ModOptionsPage(x, y, width, height, registry)
        ));

        harmony.Patch(
            original: AccessTools.Constructor(typeof(GameMenu), new[] { typeof(bool) }),
            postfix: new HarmonyMethod(typeof(GameMenuTab), nameof(After_Constructor))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.getTabNumberFromName)),
            postfix: new HarmonyMethod(typeof(GameMenuTab), nameof(After_GetTabNumberFromName))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.draw), new[] { typeof(SpriteBatch) }),
            prefix: new HarmonyMethod(typeof(GameMenuTab), nameof(Before_Draw)),
            finalizer: new HarmonyMethod(typeof(GameMenuTab), nameof(Finally_Draw))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Game1), nameof(Game1.drawDialogueBox), new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(string), typeof(bool), typeof(bool), typeof(int), typeof(int), typeof(int) }),
            postfix: new HarmonyMethod(typeof(GameMenuTab), nameof(After_DrawDialogueBox))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.changeTab)),
            prefix: new HarmonyMethod(typeof(GameMenuTab), nameof(Before_ChangeTab)),
            postfix: new HarmonyMethod(typeof(GameMenuTab), nameof(After_ChangeTab))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(GameMenu), nameof(GameMenu.receiveKeyPress)),
            prefix: new HarmonyMethod(typeof(GameMenuTab), nameof(Before_ReceiveKeyPress))
        );
    }

    /// <summary>Add a tab before the options tab. It appears the next time the game menu opens.</summary>
    public static void AddBeforeOptions(ModMenuTab tab)
    {
        if (Tabs.Any(existing => existing.Name == tab.Name))
            return;

        int index = Tabs.FindIndex(existing => existing.Name == TabName);
        Tabs.Insert(index < 0 ? Tabs.Count : index, tab);
    }

    /// <summary>Remove a tab added with <see cref="AddBeforeOptions"/>. It disappears the next time the game menu opens.</summary>
    public static void Remove(string name)
    {
        Tabs.RemoveAll(tab => tab.Name == name);
    }

    /// <summary>Tell the mod's pages of a closed menu that they were left, so none keeps the keyboard.</summary>
    public static void NotifyClosed(GameMenu menu)
    {
        foreach (IClickableMenu page in menu.pages)
            (page as IModMenuPage)?.OnHidden();
    }


    /*********
    ** Patches
    *********/
    private static void After_Constructor(GameMenu __instance)
    {
        try
        {
            // tab indexes must match page indexes
            if (__instance.tabs.Count != __instance.pages.Count)
            {
                Monitor.LogOnce($"Can't add the menu tabs: the game menu has {__instance.tabs.Count} tabs but {__instance.pages.Count} pages (probably changed by another mod).", LogLevel.Warn);
                return;
            }

            for (int i = 0; i < Tabs.Count && i < MaxTabs; i++)
            {
                ModMenuTab definition = Tabs[i];
                ClickableComponent lastTab = __instance.tabs[^1];
                ClickableComponent tab = new(
                    new Rectangle(lastTab.bounds.X + 64, lastTab.bounds.Y, 64, 64),
                    definition.Name,
                    definition.GetLabel()
                )
                {
                    myID = FirstTabComponentId + i,
                    leftNeighborID = lastTab.myID,
                    tryDefaultIfNoDownNeighborExists = true,
                    fullyImmutable = true
                };
                __instance.tabs.Add(tab);
                lastTab.rightNeighborID = tab.myID;

                __instance.pages.Add(definition.CreatePage(__instance.xPositionOnScreen, __instance.yPositionOnScreen, __instance.width, __instance.height));

                // the constructor already registered the tabs for controller navigation on the initial page
                __instance.GetCurrentPage()?.allClickableComponents?.Add(tab);
            }
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Failed to add the menu tabs:\n{ex}", LogLevel.Error);
        }
    }

    private static void After_GetTabNumberFromName(GameMenu __instance, string name, ref int __result)
    {
        if (__result == -1 && Tabs.Any(tab => tab.Name == name))
            __result = __instance.tabs.FindIndex(tab => tab.name == name);
    }

    /// <summary>Tell our page it's being left when the player switches to another tab.</summary>
    private static void Before_ChangeTab(GameMenu __instance, int whichTab)
    {
        if (__instance.currentTab != whichTab && __instance.GetCurrentPage() is IModMenuPage page)
            page.OnHidden();
    }

    /// <summary>Tell our page it's shown once the player switched to it.</summary>
    private static void After_ChangeTab(GameMenu __instance)
    {
        if (__instance.GetCurrentPage() is IModMenuPage page)
            page.OnShown();
    }

    /// <summary>While the player types in one of our pages, only Escape still closes the menu.</summary>
    /// <returns>Returns whether to run the game's own key handling.</returns>
    private static bool Before_ReceiveKeyPress(GameMenu __instance, Keys key)
    {
        if (__instance.GetCurrentPage() is not IModMenuPage { CapturesKeyboard: true } page)
            return true;

        // Escape closes the menu as usual, after releasing the keyboard
        if (key == Keys.Escape)
        {
            page.OnHidden();
            return true;
        }

        // every other key goes to the page alone, which leaves letters to its text box
        ((IClickableMenu)page).receiveKeyPress(key);
        return false;
    }

    private static void Before_Draw(GameMenu __instance)
    {
        PendingIconDraw = __instance.invisible ? null : __instance;
    }

    /// <summary>Clear the pending icons even if the draw throws, so they're never drawn on an unrelated dialogue box.</summary>
    private static void Finally_Draw()
    {
        PendingIconDraw = null;
    }

    private static void After_DrawDialogueBox()
    {
        GameMenu? menu = PendingIconDraw;
        if (menu is null)
            return;
        PendingIconDraw = null;

        try
        {
            SpriteBatch b = Game1.spriteBatch;

            foreach (ModMenuTab definition in Tabs)
            {
                int index = menu.tabs.FindIndex(tab => tab.name == definition.Name);
                if (index < 0)
                    continue;

                ClickableComponent tab = menu.tabs[index];
                int offsetY = menu.currentTab == index ? 8 : 0;

                // blank tab background (the vanilla skills tab, which has its portrait drawn separately)
                b.Draw(Game1.mouseCursors, new Vector2(tab.bounds.X, tab.bounds.Y + offsetY), new Rectangle(16, 368, 16, 16), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.0001f);

                // icon, fitted into the same 40px square whatever its sprite size
                (Texture2D texture, Rectangle source) = definition.GetIcon();
                float scale = 40f / Math.Max(source.Width, source.Height);
                b.Draw(texture, new Vector2(tab.bounds.X + 12, tab.bounds.Y + 14 + offsetY), source, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.00011f);
            }
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Failed to draw the menu tab icons:\n{ex}", LogLevel.Error);
        }
    }
}
