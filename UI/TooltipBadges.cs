using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>A status stamp drawn in an item's tooltip: an icon plus a ticked or empty checkbox.</summary>
/// <param name="Texture">The icon's texture.</param>
/// <param name="Source">The icon within the texture.</param>
/// <param name="Done">Whether the checkbox is ticked.</param>
internal record TooltipBadge(Texture2D Texture, Rectangle Source, bool Done);

/// <summary>Draws status stamps in the bottom-right corner of item tooltips.</summary>
/// <remarks>
/// The game computes the tooltip's position internally, so rather than recomputing it (and risking a mismatch), we
/// watch it draw the tooltip box and reuse the exact area it used.
/// </remarks>
internal static class TooltipBadges
{
    /*********
    ** Fields
    *********/
    private const float Scale = 3f;
    private const int Gap = 6;
    private const int Margin = 16;

    /// <summary>The height of one stamp row, driven by the junimo note and museum icons (16px at <see cref="Scale"/>).</summary>
    private const int RowHeight = 48;

    /// <summary>The badge providers registered by features.</summary>
    private static readonly List<Func<Item, TooltipBadge?>> Providers = new();

    private static IMonitor Monitor = null!;

    /// <summary>The item in the tooltip currently being drawn, if any.</summary>
    private static Item? CurrentItem;

    /// <summary>The area of the tooltip box being drawn.</summary>
    private static Rectangle? BoxArea;


    /*********
    ** Public methods
    *********/
    /// <summary>Apply the patches which track tooltip drawing. Badges only appear while a feature has registered one.</summary>
    public static void Apply(Harmony harmony, IMonitor monitor)
    {
        Monitor = monitor;

        harmony.Patch(
            original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.drawHoverText), new[] { typeof(SpriteBatch), typeof(StringBuilder), typeof(SpriteFont), typeof(int), typeof(int), typeof(int), typeof(string), typeof(int), typeof(string[]), typeof(Item), typeof(int), typeof(string), typeof(int), typeof(int), typeof(int), typeof(float), typeof(CraftingRecipe), typeof(IList<Item>), typeof(Texture2D), typeof(Rectangle?), typeof(Color?), typeof(Color?), typeof(float), typeof(int), typeof(int) }),
            prefix: new HarmonyMethod(typeof(TooltipBadges), nameof(Before_DrawHoverText)),
            postfix: new HarmonyMethod(typeof(TooltipBadges), nameof(After_DrawHoverText))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Item), nameof(Item.getExtraSpaceNeededForTooltipSpecialIcons)),
            postfix: new HarmonyMethod(typeof(TooltipBadges), nameof(After_GetExtraSpaceNeeded))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(IClickableMenu), nameof(IClickableMenu.drawTextureBox), new[] { typeof(SpriteBatch), typeof(Texture2D), typeof(Rectangle), typeof(int), typeof(int), typeof(int), typeof(int), typeof(Color), typeof(float), typeof(bool), typeof(float) }),
            postfix: new HarmonyMethod(typeof(TooltipBadges), nameof(After_DrawTextureBox))
        );
    }

    /// <summary>Add a badge provider, which is asked for a badge for each hovered item.</summary>
    public static void AddProvider(Func<Item, TooltipBadge?> provider)
    {
        Providers.Add(provider);
    }

    /// <summary>Remove a badge provider added by <see cref="AddProvider"/>.</summary>
    public static void RemoveProvider(Func<Item, TooltipBadge?> provider)
    {
        Providers.Remove(provider);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Reserve room at the bottom of the tooltip for the stamps, the same way the game does for its own extra icons.</summary>
    private static void After_GetExtraSpaceNeeded(Item __instance, int startingHeight, ref Point __result)
    {
        int rows = CountBadges(__instance);
        if (rows == 0)
            return;

        // the game only uses a non-zero value, so return the full height rather than a difference
        int baseHeight = __result.Y != 0 ? __result.Y : startingHeight;
        __result.Y = baseHeight + rows * (RowHeight + Gap) + Gap;
    }

    /// <summary>Count the stamps which apply to an item.</summary>
    private static int CountBadges(Item item)
    {
        int count = 0;
        foreach (Func<Item, TooltipBadge?> provider in Providers)
        {
            if (provider(item) != null)
                count++;
        }
        return count;
    }

    private static void Before_DrawHoverText(Item hoveredItem)
    {
        CurrentItem = Providers.Count > 0 ? hoveredItem : null;
        BoxArea = null;
    }

    /// <summary>Remember the first box drawn for the tooltip, which is the main frame.</summary>
    private static void After_DrawTextureBox(int x, int y, int width, int height)
    {
        if (CurrentItem != null && BoxArea is null)
            BoxArea = new Rectangle(x, y, width, height);
    }

    private static void After_DrawHoverText(SpriteBatch b)
    {
        Item? item = CurrentItem;
        Rectangle? box = BoxArea;
        CurrentItem = null;
        BoxArea = null;

        if (item is null || box is null)
            return;

        try
        {
            int row = 0;
            foreach (Func<Item, TooltipBadge?> provider in Providers)
            {
                if (provider(item) is not TooltipBadge badge)
                    continue;

                Draw(b, badge, box.Value, row);
                row++;
            }
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Failed to draw tooltip badges:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw one badge, stacking upwards from the bottom-right corner of the tooltip.</summary>
    private static void Draw(SpriteBatch b, TooltipBadge badge, Rectangle box, int row)
    {
        Rectangle checkboxSource = badge.Done ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
        int iconWidth = (int)(badge.Source.Width * Scale);
        int iconHeight = (int)(badge.Source.Height * Scale);
        int checkboxSize = (int)(checkboxSource.Width * Scale);
        int rowHeight = RowHeight;

        int right = box.Right - Margin;
        int top = box.Bottom - Margin - rowHeight - row * (rowHeight + Gap);

        b.Draw(Game1.mouseCursors, new Vector2(right - checkboxSize, top + (rowHeight - checkboxSize) / 2f), checkboxSource, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);
        b.Draw(badge.Texture, new Vector2(right - checkboxSize - Gap - iconWidth, top + (rowHeight - iconHeight) / 2f), badge.Source, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);
    }
}
