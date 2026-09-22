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
/// <param name="Done">Whether the checkbox is ticked, or <c>null</c> to show <paramref name="Label"/> instead.</param>
/// <param name="Label">A short label drawn in place of the checkbox, like the key which opens a window.</param>
/// <param name="Text">A line drawn to the left of the icon, in the mod's own colour so it stands out from the description.</param>
/// <param name="TextColor">The colour of <paramref name="Text"/>.</param>
internal record TooltipBadge(Texture2D Texture, Rectangle Source, bool? Done = null, string? Label = null, string? Text = null, Color? TextColor = null);

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
    private static void After_GetExtraSpaceNeeded(Item __instance, int minWidth, int horizontalBuffer, int startingHeight, ref Point __result)
    {
        int rows = 0;
        float widest = 0;
        foreach (Func<Item, TooltipBadge?> provider in Providers)
        {
            if (provider(__instance) is not TooltipBadge badge)
                continue;

            rows++;
            widest = Math.Max(widest, GetWidth(badge));
        }

        if (rows == 0)
            return;

        // the game only uses a non-zero value, so return the full size rather than a difference
        int baseHeight = __result.Y != 0 ? __result.Y : startingHeight;
        __result.Y = baseHeight + rows * (RowHeight + Gap) + Gap;
        __result.X = Math.Max(__result.X != 0 ? __result.X : minWidth, (int)widest + Margin * 2 + horizontalBuffer);
    }

    /// <summary>Get how wide a stamp is, so the tooltip can be widened to fit it.</summary>
    private static float GetWidth(TooltipBadge badge)
    {
        float width = badge.Source.Width * Scale;

        if (badge.Done is not null)
            width += Gap + OptionsCheckbox.sourceRectChecked.Width * Scale;
        else if (badge.Label != null)
            width += Gap + Game1.smallFont.MeasureString(badge.Label).X;

        if (badge.Text != null)
            width += Gap + Game1.smallFont.MeasureString(badge.Text).X;

        return width;
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
        int iconWidth = (int)(badge.Source.Width * Scale);
        int iconHeight = (int)(badge.Source.Height * Scale);
        int rowHeight = RowHeight;

        int right = box.Right - Margin;
        int top = box.Bottom - Margin - rowHeight - row * (rowHeight + Gap);

        // right side: either the ticked/empty box, or a label such as the key which opens a window
        int rightWidth;
        if (badge.Done is bool done)
        {
            Rectangle checkboxSource = done ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
            rightWidth = (int)(checkboxSource.Width * Scale);
            b.Draw(Game1.mouseCursors, new Vector2(right - rightWidth, top + (rowHeight - rightWidth) / 2f), checkboxSource, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);
        }
        else
        {
            string label = badge.Label ?? "";
            Vector2 size = Game1.smallFont.MeasureString(label);
            rightWidth = (int)size.X;
            Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(right - rightWidth, top + (rowHeight - size.Y) / 2f), Game1.textColor);
        }

        float iconLeft = right - rightWidth - Gap - iconWidth;
        b.Draw(badge.Texture, new Vector2(iconLeft, top + (rowHeight - iconHeight) / 2f), badge.Source, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 1f);

        // our own line, drawn in our colour: the game's description text is all one colour, so this is what makes it stand out
        if (badge.Text != null)
        {
            Vector2 size = Game1.smallFont.MeasureString(badge.Text);
            Utility.drawTextWithShadow(b, badge.Text, Game1.smallFont, new Vector2(iconLeft - Gap - size.X, top + (rowHeight - size.Y) / 2f), badge.TextColor ?? Game1.textColor);
        }
    }
}
