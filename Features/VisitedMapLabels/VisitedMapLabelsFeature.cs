using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.WorldMaps;

namespace PelicanMemory.Features.VisitedMapLabels;

/// <summary>Names the areas of the world map (Mountains, Pelican Town, Cindersap Forest…) the player has already been to.</summary>
/// <remarks>
/// Buildings are recognisable from the map drawing itself and the vanilla hover tooltip; what's missing is the name
/// of the areas, which quests and fishing events refer to. There are about a dozen of them, well spread out, so the
/// names stay readable while shown permanently.
/// </remarks>
internal class VisitedMapLabelsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The padding around the text inside a label.</summary>
    private const int PaddingX = 8;
    private const int PaddingY = 4;

    /// <summary>How opaque the name labels are, so they sit behind the map rather than on top of it.</summary>
    private const float LabelOpacity = 0.75f;

    /// <summary>The config key for the toggle above the map.</summary>
    private const string VisibleKey = "map-labels.visible";

    /// <summary>The vertical offsets tried when a label overlaps one already placed, in pixels.</summary>
    private static readonly int[] VerticalOffsets = { 0, -28, 28, -56, 56 };

    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static VisitedMapLabelsFeature? Instance;

    /// <summary>The toggle button above the map, updated each time it's drawn.</summary>
    private Rectangle ToggleButton;

    /// <summary>Whether the names are currently shown.</summary>
    private bool ShowLabels => this.Settings.GetFlag(VisibleKey, true);

    /// <summary>The labels for each map page. A page is rebuilt every time the map is opened, and visited places can't change while it's open.</summary>
    private readonly ConditionalWeakTable<MapPage, List<Label>> LabelCache = new();

    /// <param name="Text">The area name.</param>
    /// <param name="Box">The background area.</param>
    /// <param name="TextPosition">Where the text is drawn.</param>
    private record Label(string Text, Rectangle Box, Vector2 TextPosition);


    /*********
    ** Public methods
    *********/
    public override string Id => "visited-map-labels";

    public VisitedMapLabelsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Postfix(
            AccessTools.Method(typeof(MapPage), nameof(MapPage.drawMap), new[] { typeof(SpriteBatch), typeof(bool), typeof(float) }),
            typeof(VisitedMapLabelsFeature),
            nameof(After_DrawMap)
        );
        this.Prefix(
            AccessTools.Method(typeof(MapPage), nameof(MapPage.receiveLeftClick)),
            typeof(VisitedMapLabelsFeature),
            nameof(Before_ReceiveLeftClick)
        );
        this.Postfix(
            AccessTools.Method(typeof(MapPage), nameof(MapPage.performHoverAction)),
            typeof(VisitedMapLabelsFeature),
            nameof(After_PerformHoverAction)
        );
    }

    protected override void OnDisable()
    {
        this.LabelCache.Clear();
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the labels on top of the map textures, but under the player portraits, the scroll and the hover tooltip.</summary>
    private static void After_DrawMap(MapPage __instance, SpriteBatch b, float alpha)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            if (Instance.ShowLabels)
            {
                foreach (Label label in Instance.LabelCache.GetValue(__instance, Instance.BuildLabels))
                {
                    float opacity = alpha * LabelOpacity;
                    b.Draw(Game1.staminaRect, label.Box, Color.Black * (0.7f * opacity));
                    Utility.drawTextWithShadow(b, label.Text, Game1.smallFont, label.TextPosition, Color.White * opacity, 0.75f, shadowIntensity: opacity);
                }
            }

            Instance.DrawToggle(b, __instance, alpha);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw map labels:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw the show/hide toggle above the map, using the vanilla checkbox.</summary>
    private void DrawToggle(SpriteBatch b, MapPage page, float alpha)
    {
        Rectangle source = this.ShowLabels ? OptionsCheckbox.sourceRectChecked : OptionsCheckbox.sourceRectUnchecked;
        int size = source.Width * 4;
        string label = this.Helper.Translation.Get("map.toggle-labels");
        Vector2 textSize = Game1.smallFont.MeasureString(label) * 0.75f;

        this.ToggleButton = new Rectangle(page.mapBounds.X, page.mapBounds.Y - size - 16, size + 8 + (int)textSize.X, size);

        b.Draw(Game1.staminaRect, new Rectangle(this.ToggleButton.X - 6, this.ToggleButton.Y - 4, this.ToggleButton.Width + 12, this.ToggleButton.Height + 8), Color.Black * (0.55f * alpha));
        b.Draw(Game1.mouseCursors, new Vector2(this.ToggleButton.X, this.ToggleButton.Y), source, Color.White * alpha, 0f, Vector2.Zero, 4f, SpriteEffects.None, 1f);
        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(this.ToggleButton.X + size + 8, this.ToggleButton.Y + (size - textSize.Y) / 2), Color.White * alpha, 0.75f, shadowIntensity: alpha);
    }

    /// <summary>Toggle the names when the button is clicked, without letting the click close the map.</summary>
    private static bool Before_ReceiveLeftClick(MapPage __instance, int x, int y)
    {
        if (Instance is null || !Instance.ToggleButton.Contains(x, y))
            return true;

        Instance.Settings.SetFlag(VisibleKey, !Instance.ShowLabels);
        Game1.playSound("drumkit6");
        return false;
    }

    /// <summary>Describe the button when the cursor is over it.</summary>
    private static void After_PerformHoverAction(MapPage __instance, int x, int y)
    {
        if (Instance is not null && Instance.ToggleButton.Contains(x, y))
            __instance.hoverText = Instance.Helper.Translation.Get("map.toggle-labels.description");
    }

    /// <summary>Build the labels for a map page.</summary>
    private List<Label> BuildLabels(MapPage page)
    {
        List<Label> labels = new();
        List<Rectangle> placed = new();

        foreach (MapArea area in page.mapAreas)
        {
            string name = this.GetAreaName(area);
            if (name.Length == 0)
                continue;

            if (!WorldMapLookup.HasVisited(Game1.player, area))
                continue;

            if (!TryGetScreenArea(page, area, out Rectangle screenArea))
                continue;

            Vector2 size = Game1.smallFont.MeasureString(name) * 0.75f;
            int width = (int)size.X + PaddingX * 2;
            int height = (int)size.Y + PaddingY * 2;

            foreach (int offsetY in VerticalOffsets)
            {
                Rectangle box = new(screenArea.Center.X - width / 2, screenArea.Center.Y - height / 2 + offsetY, width, height);
                if (placed.Any(other => other.Intersects(box)))
                    continue;

                placed.Add(box);
                labels.Add(new Label(name, box, new Vector2(box.X + PaddingX, box.Y + PaddingY)));
                break;
            }
        }

        return labels;
    }

    /// <summary>Get an area's name, or an empty string if it has none.</summary>
    private string GetAreaName(MapArea area)
    {
        // a name from the mod's translations, for areas the game doesn't name (Ginger Island)
        Translation custom = this.Helper.Translation.Get($"map-area.{area.Region.Id}.{area.Id}");
        if (custom.HasValue())
            return custom;

        // the name shown on the scroll at the bottom of the map when the player is there
        string name = GetFirstLine(area.GetScrollText() ?? "");
        if (name.Length > 0)
            return name;

        // else an area-wide tooltip, e.g. 'Railroad' or 'Calico Desert'
        foreach (MapAreaTooltip tooltip in area.GetTooltips())
        {
            if (tooltip.Data.Id == "Default" || tooltip.Data.Id == area.Id)
                return GetFirstLine(tooltip.Text);
        }

        return "";
    }

    /// <summary>Get where an area is drawn on screen.</summary>
    private static bool TryGetScreenArea(MapPage page, MapArea area, out Rectangle screenArea)
    {
        Rectangle pixelArea = area.Data.PixelArea;
        if (!pixelArea.IsEmpty)
        {
            screenArea = new Rectangle(page.mapBounds.X + pixelArea.X * 4, page.mapBounds.Y + pixelArea.Y * 4, pixelArea.Width * 4, pixelArea.Height * 4);
            return true;
        }

        // else fall back to the area covered by its tooltips
        Rectangle? union = null;
        foreach (MapAreaTooltip tooltip in area.GetTooltips())
        {
            Rectangle tooltipArea = tooltip.GetPixelArea();
            union = union is null ? tooltipArea : Rectangle.Union(union.Value, tooltipArea);
        }

        if (union is null)
        {
            screenArea = Rectangle.Empty;
            return false;
        }

        screenArea = new Rectangle(page.mapBounds.X + union.Value.X, page.mapBounds.Y + union.Value.Y, union.Value.Width, union.Value.Height);
        return true;
    }

    /// <summary>Get the first line of a text, which may have extra lines like opening hours.</summary>
    private static string GetFirstLine(string text)
    {
        string line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line == "???" ? "" : line;
    }
}
