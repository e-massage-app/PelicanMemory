using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.GameData.Locations;

namespace PelicanMemory.Features.CaughtFishTooltip;

/// <summary>Where a fish can be caught.</summary>
internal enum WaterType
{
    Freshwater,
    Ocean,
    Mines
}

/// <summary>The official catch conditions for a fish, filtered to what the player can already know.</summary>
/// <param name="IsCrabPot">Whether the fish is caught in crab pots (no season, weather or time constraints).</param>
/// <param name="WaterTypes">The water types, or empty if unknown.</param>
/// <param name="Seasons">The seasons in which the fish can be caught.</param>
/// <param name="Weather">The raw weather value from <c>Data/Fish</c> (<c>sunny</c>, <c>rainy</c> or <c>both</c>).</param>
/// <param name="TimeRanges">The time ranges (start inclusive, end exclusive) in 26-hour game time.</param>
/// <param name="LocationNames">The internal names of the places where it can be caught, limited to places the player has visited.</param>
internal record FishInfo(bool IsCrabPot, IReadOnlyCollection<WaterType> WaterTypes, IReadOnlyCollection<Season> Seasons, string Weather, IReadOnlyList<(int Start, int End)> TimeRanges, IReadOnlyCollection<string> LocationNames);

/// <summary>One place and season in which a fish can be caught.</summary>
/// <param name="LocationName">The location's name in <see cref="Farmer.locationsVisited"/>.</param>
internal record FishSpawn(string LocationName, WaterType WaterType, Season[] Seasons);

/// <summary>Reads fish catch conditions from the game data.</summary>
/// <remarks>
/// Since 1.6, <c>Data/Fish</c> only provides time and weather; seasons and places come from each location's
/// <c>Fish</c> list in <c>Data/Locations</c>. To avoid spoilers, seasons and water types only consider locations the
/// player has visited (e.g. the island's all-year pufferfish stays hidden until the player has been there).
/// </remarks>
internal class FishInfoResolver
{
    /*********
    ** Fields
    *********/
    private static readonly Season[] AllSeasons = { Season.Spring, Season.Summer, Season.Fall, Season.Winter };

    /// <summary>Locations whose fish list isn't a real fishing spot (defaults, festivals, farm types inheriting other locations).</summary>
    private static readonly HashSet<string> IgnoredLocations = new(StringComparer.OrdinalIgnoreCase) { "Default", "Temp", "fishingGame" };

    /// <summary>Saltwater locations.</summary>
    private static readonly HashSet<string> OceanLocations = new(StringComparer.OrdinalIgnoreCase) { "Beach", "BeachNightMarket", "Submarine", "IslandSouth", "IslandSouthEast", "IslandSouthEastCave" };

    /// <summary>Underground locations.</summary>
    private static readonly HashSet<string> MineLocations = new(StringComparer.OrdinalIgnoreCase) { "UndergroundMine", "Caldera" };

    /// <summary>Fish hardcoded in <c>MineShaft.getFish</c> for specific mine levels, which don't appear in <c>Data/Locations</c>.</summary>
    private static readonly string[] HardcodedMineFish = { "(O)158", "(O)161", "(O)162" };

    /// <summary>The spawn entries indexed by qualified fish ID, built on first use.</summary>
    private Dictionary<string, List<FishSpawn>>? SpawnsByFish;



    /*********
    ** Public methods
    *********/
    /// <summary>Clear cached data, so it's reloaded next time.</summary>
    public void Reset()
    {
        this.SpawnsByFish = null;
    }

    /// <summary>Get the catch conditions for a fish, if it has fish data.</summary>
    /// <param name="qualifiedItemId">The qualified item ID.</param>
    /// <param name="hasVisited">Get whether the player has visited a location, by name.</param>
    public FishInfo? TryGetInfo(string qualifiedItemId, Func<string, bool> hasVisited)
    {
        if (!qualifiedItemId.StartsWith(ItemRegistry.type_object))
            return null;

        string localId = qualifiedItemId.Substring(ItemRegistry.type_object.Length);
        FishSpawn[] knownSpawns = this.GetSpawns(qualifiedItemId)
            .Where(spawn => hasVisited(spawn.LocationName))
            .ToArray();

        // no fish data (e.g. jellies): location data only, which ignores time and weather
        if (!DataLoader.Fish(Game1.content).TryGetValue(localId, out string? rawData))
        {
            return knownSpawns.Length > 0
                ? new FishInfo(IsCrabPot: false, GetWaterTypes(knownSpawns), GetSeasons(knownSpawns), Weather: "both", Array.Empty<(int, int)>(), GetLocationNames(knownSpawns))
                : null;
        }

        string[] fields = rawData.Split('/');

        // crab pot fish: Name/trap/chance/.../waterType/minSize/maxSize/...
        if (fields.Length > 4 && fields[1] == "trap")
        {
            WaterType[] waterTypes = fields[4] switch
            {
                "ocean" => new[] { WaterType.Ocean },
                "freshwater" => new[] { WaterType.Freshwater },
                _ => Array.Empty<WaterType>()
            };
            return new FishInfo(IsCrabPot: true, waterTypes, AllSeasons, Weather: "both", Array.Empty<(int, int)>(), GetLocationNames(knownSpawns));
        }

        // rod fish: Name/difficulty/behavior/minSize/maxSize/times/seasons/weather/...
        if (fields.Length < 8)
            return null;

        IReadOnlyCollection<WaterType> water;
        IReadOnlyCollection<Season> seasons;
        if (knownSpawns.Length > 0)
        {
            water = GetWaterTypes(knownSpawns);
            seasons = GetSeasons(knownSpawns);
        }
        else
        {
            // no known spawn location (e.g. caught somewhere with no location data): use the fish's own data, without revealing places
            water = Array.Empty<WaterType>();
            seasons = ParseSeasonNames(fields[6].Split(' ', StringSplitOptions.RemoveEmptyEntries)) ?? AllSeasons;
        }

        return new FishInfo(IsCrabPot: false, water, seasons, Weather: fields[7], ParseTimeRanges(fields[5]), GetLocationNames(knownSpawns));
    }


    /*********
    ** Private methods
    *********/
    private static WaterType[] GetWaterTypes(IEnumerable<FishSpawn> spawns) => spawns.Select(p => p.WaterType).Distinct().OrderBy(p => p).ToArray();

    private static Season[] GetSeasons(IEnumerable<FishSpawn> spawns) => spawns.SelectMany(p => p.Seasons).Distinct().OrderBy(p => p).ToArray();

    private static string[] GetLocationNames(IEnumerable<FishSpawn> spawns) => spawns.Select(p => p.LocationName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Get the spawn entries for a fish, across every location.</summary>
    public IEnumerable<FishSpawn> GetSpawns(string qualifiedItemId)
    {
        this.SpawnsByFish ??= BuildSpawnIndex();
        return this.SpawnsByFish.TryGetValue(qualifiedItemId, out List<FishSpawn>? spawns)
            ? spawns
            : Enumerable.Empty<FishSpawn>();
    }

    /// <summary>Index every fish spawn entry in <c>Data/Locations</c>.</summary>
    private static Dictionary<string, List<FishSpawn>> BuildSpawnIndex()
    {
        Dictionary<string, List<FishSpawn>> index = new(StringComparer.OrdinalIgnoreCase);

        void Add(string fishId, FishSpawn spawn)
        {
            if (!index.TryGetValue(fishId, out List<FishSpawn>? list))
                index[fishId] = list = new List<FishSpawn>();
            list.Add(spawn);
        }

        foreach ((string locationName, LocationData location) in DataLoader.Locations(Game1.content))
        {
            if (IgnoredLocations.Contains(locationName) || locationName.StartsWith("Farm_", StringComparison.OrdinalIgnoreCase) || location.Fish is null)
                continue;

            string visitedName = GetVisitedName(locationName);
            foreach (SpawnFishData entry in location.Fish)
            {
                FishSpawn spawn = new(visitedName, GetWaterType(locationName, location, entry), GetSeasons(entry));

                if (entry.ItemId != null && entry.ItemId.StartsWith(ItemRegistry.type_object))
                    Add(entry.ItemId, spawn);

                if (entry.RandomItemId != null)
                {
                    foreach (string itemId in entry.RandomItemId.Where(id => id.StartsWith(ItemRegistry.type_object)))
                        Add(itemId, spawn);
                }
            }
        }

        foreach (string fishId in HardcodedMineFish)
            Add(fishId, new FishSpawn(GetVisitedName("UndergroundMine"), WaterType.Mines, AllSeasons));

        return index;
    }

    /// <summary>Get the name recorded in <see cref="Farmer.locationsVisited"/> for a data location.</summary>
    private static string GetVisitedName(string locationName)
    {
        // mine levels aren't recorded, only the entrance is
        return locationName.Equals("UndergroundMine", StringComparison.OrdinalIgnoreCase) ? "Mine" : locationName;
    }

    /// <summary>Get the water type for a spawn entry.</summary>
    private static WaterType GetWaterType(string locationName, LocationData location, SpawnFishData entry)
    {
        if (entry.FishAreaId != null)
        {
            if (entry.FishAreaId.Contains("Ocean", StringComparison.OrdinalIgnoreCase))
                return WaterType.Ocean;
            if (entry.FishAreaId.Contains("Freshwater", StringComparison.OrdinalIgnoreCase))
                return WaterType.Freshwater;
        }

        if (OceanLocations.Contains(locationName))
            return WaterType.Ocean;
        if (MineLocations.Contains(locationName))
            return WaterType.Mines;

        // locations with only ocean fishing areas (e.g. from mods)
        if (location.FishAreas?.Count > 0 && location.FishAreas.Keys.All(key => key.Contains("Ocean", StringComparison.OrdinalIgnoreCase)))
            return WaterType.Ocean;

        return WaterType.Freshwater;
    }

    /// <summary>Get the seasons in which a spawn entry applies.</summary>
    private static Season[] GetSeasons(SpawnFishData entry)
    {
        if (entry.Season.HasValue)
            return new[] { entry.Season.Value };

        if (!string.IsNullOrWhiteSpace(entry.Condition))
        {
            foreach (string clause in entry.Condition.Split(','))
            {
                string[] args = clause.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Season[]? seasons = args.FirstOrDefault()?.ToUpperInvariant() switch
                {
                    "LOCATION_SEASON" => ParseSeasonNames(args.Skip(2)), // LOCATION_SEASON <location> <seasons>+
                    "SEASON" => ParseSeasonNames(args.Skip(1)),          // SEASON <seasons>+
                    _ => null
                };
                if (seasons != null)
                    return seasons;
            }
        }

        return AllSeasons;
    }

    /// <summary>Parse season names like <c>spring</c>, or <c>null</c> if none are valid.</summary>
    private static Season[]? ParseSeasonNames(IEnumerable<string> names)
    {
        Season[] seasons = names
            .Select(name => Enum.TryParse(name, ignoreCase: true, out Season season) ? season : (Season?)null)
            .OfType<Season>()
            .ToArray();
        return seasons.Length > 0 ? seasons : null;
    }

    /// <summary>Parse a <c>Data/Fish</c> time field like <c>600 1000 1800 2600</c>.</summary>
    private static (int Start, int End)[] ParseTimeRanges(string field)
    {
        int[] values = field
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out int time) ? time : -1)
            .ToArray();

        List<(int, int)> ranges = new();
        for (int i = 0; i + 1 < values.Length; i += 2)
        {
            if (values[i] >= 0 && values[i + 1] > values[i])
                ranges.Add((values[i], values[i + 1]));
        }
        return ranges.ToArray();
    }
}
