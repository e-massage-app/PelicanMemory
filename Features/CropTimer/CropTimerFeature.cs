using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Crops;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace PelicanMemory.Features.CropTimer;

/// <summary>Resting the cursor on a crop says how many days before it can be harvested, and warns if the season will kill it first.</summary>
/// <remarks>
/// The game counts each crop's growth but never shows it. Everything here is read from the player's own crop: the
/// days left already include their fertiliser and profession, and only a watered night makes a crop grow. Shown only
/// while the cursor is on the crop, so nothing is added to the screen otherwise.
/// </remarks>
internal class CropTimerFeature : FeatureBase
{
    /*********
    ** Public methods
    *********/
    public override string Id => "crop-timer";

    public CropTimerFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        WorldTooltip.AddProvider(this.GetTip);
    }

    protected override void OnDisable()
    {
        WorldTooltip.RemoveProvider(this.GetTip);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Describe the crop or young fruit tree on a tile.</summary>
    private WorldTip? GetTip(GameLocation location, Vector2 tile)
    {
        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
        {
            if (feature is HoeDirt { crop: not null } dirt)
                return this.DescribeCrop(location, dirt);

            if (feature is FruitTree tree)
                return this.DescribeTree(location, tree);
        }

        // a crop growing in a garden pot
        if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && obj is IndoorPot { hoeDirt.Value.crop: not null } pot)
            return this.DescribeCrop(location, pot.hoeDirt.Value);

        return null;
    }

    /// <summary>Say how long a crop still needs.</summary>
    private WorldTip? DescribeCrop(GameLocation location, HoeDirt dirt)
    {
        Crop crop = dirt.crop;

        // wild crops the player didn't plant (spring onions, ginger) say nothing
        if (crop.forageCrop.Value)
            return null;

        string name = GetCropName(crop);
        if (crop.dead.Value)
            return new WorldTip(name, this.T("crop-timer.dead"));

        int days = FarmTiming.GetDaysUntilHarvest(crop.phaseDays.ToList(), crop.currentPhase.Value, crop.dayOfCurrentPhase.Value, crop.fullyGrown.Value);
        if (days <= 0)
            return new WorldTip(name, this.T("crop-timer.ready"));

        List<string> lines = new()
        {
            this.T(crop.fullyGrown.Value ? "crop-timer.regrows" : "crop-timer.days", new { count = days })
        };

        // only a watered night makes it grow
        if (dirt.needsWatering() && !dirt.isWatered())
            lines.Add(this.T("crop-timer.not-watered"));

        if (WillDieBeforeHarvest(location, crop, days))
            lines.Add(this.T(crop.fullyGrown.Value ? "crop-timer.wont-regrow" : "crop-timer.wont-ripen"));

        return new WorldTip(name, string.Join("\n", lines));
    }

    /// <summary>Say how long a young fruit tree still needs before it bears fruit.</summary>
    private WorldTip? DescribeTree(GameLocation location, FruitTree tree)
    {
        if (tree.daysUntilMature.Value <= 0)
            return null;

        string name = ItemRegistry.GetData(ItemRegistry.QualifyItemId(tree.treeId.Value) ?? "")?.DisplayName ?? this.T("crop-timer.fruit-tree");
        int rate = Math.Max(1, tree.growthRate.Value);
        int days = (int)Math.Ceiling(tree.daysUntilMature.Value / (double)rate);

        List<string> lines = new() { this.T("crop-timer.tree-days", new { count = days }) };
        if (FruitTree.IsGrowthBlocked(tree.Tile, location))
            lines.Add(this.T("crop-timer.tree-blocked"));

        return new WorldTip(name, string.Join("\n", lines));
    }

    /// <summary>Get whether a change of season will kill a crop before its next harvest.</summary>
    /// <remarks>Only outdoors in places with seasons: the greenhouse, the island and indoor pots never kill a crop.</remarks>
    private static bool WillDieBeforeHarvest(GameLocation location, Crop crop, int days)
    {
        if (!location.IsOutdoors || location.SeedsIgnoreSeasonsHere() || location.GetSeason() != Game1.season)
            return false;

        CropData? data = crop.GetData();
        return data?.Seasons != null && FarmTiming.WillDieBeforeHarvest(days, Game1.dayOfMonth, Game1.season, data.Seasons);
    }

    /// <summary>Get the name of what a crop gives, or of its seeds if that's unknown.</summary>
    private static string GetCropName(Crop crop)
    {
        string? harvest = ItemRegistry.QualifyItemId(crop.indexOfHarvest.Value);
        if (harvest != null && ItemRegistry.GetData(harvest) is { } data)
            return data.DisplayName;

        return ItemRegistry.GetData(ItemRegistry.QualifyItemId(crop.netSeedIndex.Value) ?? "")?.DisplayName ?? "?";
    }

    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }
}
