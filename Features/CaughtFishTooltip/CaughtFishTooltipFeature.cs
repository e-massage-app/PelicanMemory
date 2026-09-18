using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.CaughtFishTooltip;

/// <summary>Adds catch conditions to the tooltip of fish the local player has already caught.</summary>
/// <remarks>
/// The text is appended to <see cref="SObject.getDescription"/> (item tooltips everywhere: inventory, chests, shops)
/// and to <see cref="CollectionsPage.createDescription"/> (the fish collection), with the vanilla look.
/// </remarks>
internal class CaughtFishTooltipFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static CaughtFishTooltipFeature? Instance;

    /// <summary>The text width used in the collections menu, matching the vanilla description there.</summary>
    private const int CollectionsTextWidth = 256;

    private readonly FishInfoResolver Resolver = new();

    /// <summary>The generated tooltip text indexed by qualified item ID and wrap width. Descriptions are requested every frame while hovering.</summary>
    private readonly Dictionary<(string ItemId, int Width), string> TextCache = new();

    /// <summary>The number of visited locations when <see cref="TextCache"/> was built, since the text depends on it.</summary>
    private int CachedVisitedCount = -1;


    /*********
    ** Public methods
    *********/
    public override string Id => "caught-fish-tooltip";

    public CaughtFishTooltipFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Helper.Events.Content.AssetsInvalidated += this.OnAssetsInvalidated;
        this.Helper.Events.Content.LocaleChanged += this.OnLocaleChanged;
        this.Helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;

        this.Postfix(
            AccessTools.Method(typeof(SObject), nameof(SObject.getDescription)),
            typeof(CaughtFishTooltipFeature),
            nameof(After_GetDescription)
        );
        this.Postfix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.createDescription)),
            typeof(CaughtFishTooltipFeature),
            nameof(After_CreateDescription)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Content.AssetsInvalidated -= this.OnAssetsInvalidated;
        this.Helper.Events.Content.LocaleChanged -= this.OnLocaleChanged;
        this.Helper.Events.GameLoop.ReturnedToTitle -= this.OnReturnedToTitle;

        this.ClearCache();
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Add the catch conditions to an item tooltip.</summary>
    private static void After_GetDescription(SObject __instance, ref string __result)
    {
        if (Instance is null || !Context.IsWorldReady || __instance.IsRecipe)
            return;

        try
        {
            int width = Instance.Helper.Reflection.GetMethod(__instance, "getDescriptionWidth").Invoke<int>();
            Append(ref __result, Instance.GetTooltipText(__instance.QualifiedItemId, width));
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to add fish info to the item tooltip:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Add the catch conditions in the collections menu, whose fish tab builds its own tooltip text.</summary>
    private static void After_CreateDescription(string id, ref string __result)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            Append(ref __result, Instance.GetTooltipText(ItemRegistry.type_object + id, CollectionsTextWidth));
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to add fish info to the collections menu:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Append the mod's text to a tooltip, separated by a blank line.</summary>
    private static void Append(ref string text, string? extra)
    {
        if (!string.IsNullOrEmpty(extra))
            text = text.TrimEnd() + "\n\n" + extra;
    }

    /// <summary>Get the wrapped catch conditions for a fish the player has caught, or <c>null</c> if there's nothing to show.</summary>
    private string? GetTooltipText(string qualifiedItemId, int width)
    {
        if (!Game1.player.fishCaught.ContainsKey(qualifiedItemId))
            return null; // only fish this player has caught

        // the text depends on the places visited so far
        int visitedCount = Game1.player.locationsVisited.Count;
        if (visitedCount != this.CachedVisitedCount)
        {
            this.TextCache.Clear();
            this.CachedVisitedCount = visitedCount;
        }

        var key = (qualifiedItemId, width);
        if (!this.TextCache.TryGetValue(key, out string? text))
        {
            FishInfo? info = this.Resolver.TryGetInfo(qualifiedItemId, Game1.player.locationsVisited.Contains);
            text = info != null
                ? Game1.parseText(this.BuildText(info), Game1.smallFont, width)
                : "";
            this.TextCache[key] = text;
        }

        return text;
    }

    /// <summary>Build the unwrapped tooltip text.</summary>
    private string BuildText(FishInfo info)
    {
        List<string> lines = new();

        // where
        if (info.LocationNames.Count > 0)
        {
            string places = string.Join(", ", info.LocationNames.Select(GetLocationName).Distinct().OrderBy(name => name));
            lines.Add(this.Helper.Translation.Get("fish-tooltip.places", new { value = places }));
        }

        if (info.WaterTypes.Count > 0)
        {
            string water = string.Join(", ", info.WaterTypes.Select(type => this.Translate($"water.{type.ToString().ToLowerInvariant()}")));
            lines.Add(this.Helper.Translation.Get("fish-tooltip.water", new { value = water }));
        }

        // how
        if (info.IsCrabPot)
        {
            lines.Add(this.Helper.Translation.Get("fish-tooltip.how", new { value = this.Translate("fish-tooltip.crab-pot") }));
            return string.Join("\n", lines);
        }

        // when
        string seasons = info.Seasons.Count >= 4
            ? this.Translate("seasons.all")
            : string.Join(", ", info.Seasons.Select(season => Utility.getSeasonNameFromNumber((int)season)));
        lines.Add(this.Helper.Translation.Get("fish-tooltip.seasons", new { value = seasons }));

        string weather = info.Weather switch
        {
            "sunny" => this.Translate("weather.sunny"),
            "rainy" => this.Translate("weather.rainy"),
            _ => this.Translate("weather.both")
        };
        lines.Add(this.Helper.Translation.Get("fish-tooltip.weather", new { value = weather }));

        string times = info.TimeRanges.Count == 0 || info.TimeRanges.All(range => range.Start <= 600 && range.End >= 2600)
            ? this.Translate("time.all")
            : string.Join(", ", info.TimeRanges.Select(range => $"{Game1.getTimeOfDayString(range.Start)} - {Game1.getTimeOfDayString(range.End)}"));
        lines.Add(this.Helper.Translation.Get("fish-tooltip.time", new { value = times }));

        return string.Join("\n", lines);
    }

    /// <summary>Get a place's display name from its internal name.</summary>
    private static string GetLocationName(string locationName)
    {
        return Game1.getLocationFromName(locationName)?.DisplayName ?? locationName;
    }

    private string Translate(string key) => this.Helper.Translation.Get(key);

    private void ClearCache()
    {
        this.TextCache.Clear();
        this.CachedVisitedCount = -1;
        this.Resolver.Reset();
    }

    private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
    {
        if (e.NamesWithoutLocale.Any(name => name.IsEquivalentTo("Data/Fish") || name.IsEquivalentTo("Data/Locations")))
            this.ClearCache();
    }

    private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
    {
        this.ClearCache();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.ClearCache();
    }
}
