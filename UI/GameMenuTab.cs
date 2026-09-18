using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>Injects the mod's options tab into the vanilla <see cref="GameMenu"/>.</summary>
/// <remarks>
/// The vanilla menu hardcodes its tabs in three places, so we patch each one:
/// <list type="bullet">
///   <item>the constructor, to add our page and tab (done in the constructor so that <c>new GameMenu(ourTab)</c>, used by the game on window resize, works);</item>
///   <item><see cref="GameMenu.getTabNumberFromName"/>, which returns -1 for unknown names;</item>
///   <item>the tab icon drawing, which ignores unknown names. We draw our icon right after the menu box, before vanilla tabs and page content, so the layering matches vanilla tabs.</item>
/// </list>
/// </remarks>
internal static class GameMenuTab
{
    /// <summary>The unique tab name.</summary>
    public const string TabName = "JordanNeau.PelicanMemory";

    /// <summary>The controller navigation ID for our tab (the vanilla ones end at <see cref="GameMenu.region_exitTab"/>).</summary>
    private const int TabComponentId = GameMenu.region_exitTab + 1;

    /// <summary>The item whose sprite is drawn on the tab (Lost Book).</summary>
    private const string IconItemId = "(O)102";

    private static IMonitor Monitor = null!;
    private static FeatureRegistry Registry = null!;

    /// <summary>The menu whose tab icon must be drawn on the next dialogue box draw.</summary>
    private static GameMenu? PendingIconDraw;

    public static void Apply(Harmony harmony, IMonitor monitor, FeatureRegistry registry)
    {
        Monitor = monitor;
        Registry = registry;

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
                Monitor.LogOnce($"Can't add the menu tab: the game menu has {__instance.tabs.Count} tabs but {__instance.pages.Count} pages (probably changed by another mod).", LogLevel.Warn);
                return;
            }

            ClickableComponent lastTab = __instance.tabs[^1];
            ClickableComponent tab = new(
                new Rectangle(lastTab.bounds.X + 64, lastTab.bounds.Y, 64, 64),
                TabName,
                Registry.Translate("menu.tab-label")
            )
            {
                myID = TabComponentId,
                leftNeighborID = lastTab.myID,
                tryDefaultIfNoDownNeighborExists = true,
                fullyImmutable = true
            };
            __instance.tabs.Add(tab);
            lastTab.rightNeighborID = TabComponentId;

            __instance.pages.Add(new ModOptionsPage(__instance.xPositionOnScreen, __instance.yPositionOnScreen, __instance.width, __instance.height, Registry));

            // the constructor already registered the tabs for controller navigation on the initial page
            __instance.GetCurrentPage()?.allClickableComponents?.Add(tab);
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Failed to add the menu tab:\n{ex}", LogLevel.Error);
        }
    }

    private static void After_GetTabNumberFromName(GameMenu __instance, string name, ref int __result)
    {
        if (__result == -1 && name == TabName)
            __result = __instance.tabs.FindIndex(tab => tab.name == TabName);
    }

    private static void Before_Draw(GameMenu __instance)
    {
        PendingIconDraw = __instance.invisible ? null : __instance;
    }

    /// <summary>Clear the pending icon even if the draw throws, so it's never drawn on an unrelated dialogue box.</summary>
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
            int index = menu.tabs.FindIndex(tab => tab.name == TabName);
            if (index < 0)
                return;

            SpriteBatch b = Game1.spriteBatch;
            ClickableComponent tab = menu.tabs[index];
            int offsetY = menu.currentTab == index ? 8 : 0;

            // blank tab background (the vanilla skills tab, which has its portrait drawn separately)
            b.Draw(Game1.mouseCursors, new Vector2(tab.bounds.X, tab.bounds.Y + offsetY), new Rectangle(16, 368, 16, 16), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0.0001f);

            // icon
            ParsedItemData icon = ItemRegistry.GetDataOrErrorItem(IconItemId);
            b.Draw(icon.GetTexture(), new Vector2(tab.bounds.X + 12, tab.bounds.Y + 14 + offsetY), icon.GetSourceRect(), Color.White, 0f, Vector2.Zero, 2.5f, SpriteEffects.None, 0.00011f);
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Failed to draw the menu tab icon:\n{ex}", LogLevel.Error);
        }
    }
}
