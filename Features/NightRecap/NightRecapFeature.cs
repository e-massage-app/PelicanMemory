using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.NightRecap;

/// <summary>A short report of the farm's morning, next to the shipping summary at night.</summary>
/// <remarks>
/// When the shipping summary shows, the night has already passed for the farm: crops have grown and machines have
/// worked until morning. So the report says what's waiting on waking: harvests, crops to water, crops the season will
/// kill, finished machines, and a tool left at Clint's. It sits beside the summary without asking for a click; on
/// nights with nothing shipped there's no summary, so the same lines come as messages on waking.
/// Only the player's own things are counted, in the places their game has.
/// </remarks>
internal class NightRecapFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The most names given after a count, like "Parsnip 8, Cauliflower 4".</summary>
    private const int NamesPerLine = 3;

    /// <summary>The narrowest panel worth drawing beside the summary; on smaller screens the messages on waking take over.</summary>
    private const int MinPanelWidth = 280;

    /// <summary>Whether a night has passed since the last report, so loading a save doesn't report anything.</summary>
    private bool NightPassed;

    /// <summary>Whether the report was shown beside the shipping summary tonight.</summary>
    private bool ShownTonight;

    /// <summary>The report for tonight, worked out once.</summary>
    private List<string>? Report;


    /*********
    ** Public methods
    *********/
    public override string Id => "night-recap";

    public NightRecapFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayEnding -= this.OnDayEnding;
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Helper.Events.Display.RenderedActiveMenu -= this.OnRenderedActiveMenu;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>A night starts: the report will be worked out once the farm has reached the morning.</summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.NightPassed = true;
        this.ShownTonight = false;
        this.Report = null;
    }

    /// <summary>On waking, give the report as messages if there was no shipping summary to show it beside.</summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!this.NightPassed)
            return;
        this.NightPassed = false;

        if (this.ShownTonight)
            return;

        try
        {
            foreach (string line in this.Report ?? this.BuildReport())
                Game1.addHUDMessage(new HUDMessage(line, HUDMessage.newQuest_type));
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to give the morning report:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw the report beside the shipping summary, once its opening animation is over.</summary>
    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (!this.NightPassed || Game1.activeClickableMenu is not ShippingMenu menu || menu.currentPage != -1)
            return;

        try
        {
            if (this.Helper.Reflection.GetField<int>(menu, "introTimer").GetValue() > 0 || this.Helper.Reflection.GetField<bool>(menu, "outro").GetValue())
                return;

            // beside the summary, in the free space on its left
            int centerX = Game1.uiViewport.Width / 2;
            int centerY = Game1.uiViewport.Height / 2;
            Rectangle area = new(32, centerY - 300, centerX - 360 - 32, 560);
            if (area.Width < MinPanelWidth)
                return;

            this.Report ??= this.BuildReport();
            if (this.Report.Count == 0)
                return;

            this.DrawPanel(e.SpriteBatch, area, this.Report);
            this.ShownTonight = true;
            menu.drawMouse(e.SpriteBatch);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the morning report:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw the report in a box like the game's own.</summary>
    private void DrawPanel(SpriteBatch b, Rectangle area, IReadOnlyList<string> lines)
    {
        SpriteFont font = Game1.smallFont;
        int textWidth = area.Width - 64;

        string title = this.T("night-recap.title");
        List<string> wrapped = lines.Select(line => Game1.parseText(line, font, textWidth)).ToList();
        int height = 32 + (int)font.MeasureString(title).Y + 16 + wrapped.Sum(text => (int)font.MeasureString(text).Y + 12) + 24;

        IClickableMenu.drawTextureBox(b, area.X, area.Y, area.Width, Math.Min(height, area.Height), Color.White);

        int y = area.Y + 32;
        Utility.drawTextWithShadow(b, title, font, new Vector2(area.X + 32, y), Game1.textColor);
        y += (int)font.MeasureString(title).Y + 16;

        foreach (string text in wrapped)
        {
            if (y > area.Bottom - 40)
                break;

            Utility.drawTextWithShadow(b, text, font, new Vector2(area.X + 32, y), Game1.textColor * 0.85f, 0.95f);
            y += (int)font.MeasureString(text).Y + 12;
        }
    }

    /// <summary>Work out what's waiting this morning, one line per kind of thing.</summary>
    private List<string> BuildReport()
    {
        Dictionary<string, int> ready = new();
        Dictionary<string, int> machines = new();
        int toWater = 0;
        int doomed = 0;

        Utility.ForEachLocation(location =>
        {
            // crops, in the ground and in garden pots
            foreach (TerrainFeature feature in location.terrainFeatures.Values)
            {
                if (feature is HoeDirt dirt)
                    CountCrop(location, dirt, ready, ref toWater, ref doomed);
            }

            foreach (SObject obj in location.objects.Values)
            {
                if (obj is IndoorPot { hoeDirt.Value: HoeDirt potDirt })
                    CountCrop(location, potDirt, ready, ref toWater, ref doomed);
                else if (IsFinishedMachine(obj))
                    machines[obj.DisplayName] = machines.GetValueOrDefault(obj.DisplayName) + 1;
            }

            return true;
        });

        List<string> report = new();
        if (ready.Count > 0)
            report.Add(this.T("night-recap.harvest", new { count = ready.Values.Sum(), names = FormatNames(ready) }));
        if (toWater > 0)
            report.Add(this.T("night-recap.water", new { count = toWater }));
        if (doomed > 0)
            report.Add(this.T("night-recap.doomed", new { count = doomed }));
        if (machines.Count > 0)
            report.Add(this.T("night-recap.machines", new { count = machines.Values.Sum(), names = FormatNames(machines) }));

        // a finished tool still waiting at the blacksmith's: the game only says so once per session
        Farmer player = Game1.player;
        if (player.toolBeingUpgraded.Value is Tool tool && player.daysLeftForToolUpgrade.Value <= 0)
            report.Add(this.T("night-recap.tool", new { tool = tool.DisplayName }));

        return report;
    }

    /// <summary>Count a crop as ready, to water, or doomed by the season.</summary>
    private static void CountCrop(GameLocation location, HoeDirt dirt, Dictionary<string, int> ready, ref int toWater, ref int doomed)
    {
        Crop? crop = dirt.crop;
        if (crop is null || crop.dead.Value || crop.forageCrop.Value)
            return;

        if (dirt.readyForHarvest())
        {
            string name = ItemRegistry.GetData(ItemRegistry.QualifyItemId(crop.indexOfHarvest.Value) ?? "")?.DisplayName ?? "?";
            ready[name] = ready.GetValueOrDefault(name) + 1;
            return;
        }

        if (dirt.needsWatering() && !dirt.isWatered())
            toWater++;

        if (location.IsOutdoors && !location.SeedsIgnoreSeasonsHere() && location.GetSeason() == Game1.season && crop.GetData() is CropData { Seasons: not null } data)
        {
            int days = FarmTiming.GetDaysUntilHarvest(crop.phaseDays.ToList(), crop.currentPhase.Value, crop.dayOfCurrentPhase.Value, crop.fullyGrown.Value);
            if (FarmTiming.WillDieBeforeHarvest(days, Game1.dayOfMonth, Game1.season, data.Seasons))
                doomed++;
        }
    }

    /// <summary>Get whether an object is a machine with something finished inside.</summary>
    private static bool IsFinishedMachine(SObject obj)
    {
        if (!obj.readyForHarvest.Value || obj.heldObject.Value is null)
            return false;

        return obj is Cask or CrabPot || obj.GetMachineData() != null;
    }

    /// <summary>Write the most common names with their counts, like "Parsnip 8, Cauliflower 4".</summary>
    private static string FormatNames(Dictionary<string, int> counts)
    {
        return string.Join(", ", counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(NamesPerLine)
            .Select(pair => $"{pair.Key} {pair.Value}"))
            + (counts.Count > NamesPerLine ? "..." : "");
    }

    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }
}
