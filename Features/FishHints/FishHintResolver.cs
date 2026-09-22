using System;
using System.Collections.Generic;
using System.Linq;
using PelicanMemory.Features.CaughtFishTooltip;
using StardewValley;

namespace PelicanMemory.Features.FishHints;

/// <summary>One condition of an unknown fish, expressed through what the player has already caught.</summary>
/// <param name="Axis">Which condition this is: <c>season</c>, <c>weather</c>, <c>time</c>, <c>place</c>, <c>level</c> or <c>fight</c>.</param>
/// <param name="CousinId">The qualified ID of a fish the player has caught under the same condition, or <c>null</c> if there is none.</param>
/// <param name="PlainKey">A phrase to use instead of a comparison, when the condition is simply "no condition".</param>
internal record FishHint(string Axis, string? CousinId, string? PlainKey);

/// <summary>Everything known about one fish, used both as a target and as a possible point of comparison.</summary>
/// <remarks>Nothing here needs the item registry, so the whole comparison can be checked outside the game.</remarks>
internal record FishProfile(
    string Id,
    string InternalName,
    IReadOnlyCollection<Season> Seasons,
    string Weather,
    IReadOnlyList<(int Start, int End)> Times,
    IReadOnlyCollection<string> Places,
    string Behaviour,
    int Difficulty,
    int MinLevel,
    bool IsCrabPot
);

/// <summary>Describes a fish the player has never caught, only by comparison with the ones they have.</summary>
/// <remarks>
/// The point is to help finish the collection without a wiki, while never handing over knowledge the player hasn't
/// earned. So every line is phrased as "the same as a fish you have already caught", and a line the player's own
/// history can't express stays unknown. A fish which only lives somewhere they have never set foot says nothing at
/// all: they couldn't have learned any of it, and it saves them hunting for something out of reach.
/// </remarks>
internal class FishHintResolver
{
    /*********
    ** Fields
    *********/
    /// <summary>Every condition compared, in the order they're shown.</summary>
    public static readonly string[] Axes = { "place", "season", "weather", "time", "level", "fight" };

    private static readonly Season[] AllSeasons = { Season.Spring, Season.Summer, Season.Fall, Season.Winter };

    /// <summary>How far two fish may differ on the difficulty scale and still fight alike.</summary>
    private const int FightTolerance = 10;

    private readonly FishInfoResolver Spawns = new();

    /// <summary>The profiles of the fish the player has caught, rebuilt when their catch list changes.</summary>
    private FishProfile[]? Caught;

    /// <summary>How many fish the last profile list was built from.</summary>
    private int CaughtCount = -1;


    /*********
    ** Public methods
    *********/
    /// <summary>Forget the cached data, so it's rebuilt next time.</summary>
    public void Reset()
    {
        this.Spawns.Reset();
        this.Caught = null;
        this.CaughtCount = -1;
    }

    /// <summary>Get whether the player has been anywhere the fish lives, which is what makes any hint possible.</summary>
    public bool IsReachable(string qualifiedItemId, Func<string, bool> hasVisited)
    {
        return this.Spawns.GetSpawns(qualifiedItemId).Any(spawn => hasVisited(spawn.LocationName));
    }

    /// <summary>Describe an uncaught fish through the fish the player has caught, one line per condition.</summary>
    /// <param name="qualifiedItemId">The fish to describe.</param>
    /// <param name="hasVisited">Get whether the player has visited a location, by name.</param>
    /// <param name="caught">The qualified IDs of the fish the player has already caught.</param>
    public IReadOnlyList<FishHint> GetHints(string qualifiedItemId, Func<string, bool> hasVisited, IReadOnlyCollection<string> caught)
    {
        FishProfile? target = this.TryGetProfile(qualifiedItemId, hasVisited);
        if (target is null)
            return Array.Empty<FishHint>();

        // rank the candidates by how much they have in common, so the same familiar fish tends to carry several lines
        FishProfile[] candidates = this.GetCaughtProfiles(caught, hasVisited)
            .Where(profile => profile.Id != target.Id)
            .OrderByDescending(profile => Axes.Count(axis => Matches(axis, target, profile)))
            .ThenBy(profile => profile.InternalName, StringComparer.Ordinal)
            .ToArray();

        List<FishHint> hints = new();
        foreach (string axis in Axes)
        {
            string? plain = GetPlainKey(axis, target);
            if (plain != null)
            {
                hints.Add(new FishHint(axis, null, plain));
                continue;
            }

            FishProfile? cousin = candidates.FirstOrDefault(profile => Matches(axis, target, profile));
            hints.Add(new FishHint(axis, cousin?.Id, null));
        }

        return hints;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the profiles of every fish the player has caught.</summary>
    private FishProfile[] GetCaughtProfiles(IReadOnlyCollection<string> caught, Func<string, bool> hasVisited)
    {
        if (this.Caught != null && this.CaughtCount == caught.Count)
            return this.Caught;

        this.CaughtCount = caught.Count;
        return this.Caught = caught
            .Select(id => this.TryGetProfile(id, hasVisited))
            .OfType<FishProfile>()
            .ToArray();
    }

    /// <summary>Build a fish's profile, or <c>null</c> if the game has no catch data for it.</summary>
    private FishProfile? TryGetProfile(string qualifiedItemId, Func<string, bool> hasVisited)
    {
        if (!qualifiedItemId.StartsWith(ItemRegistry.type_object))
            return null;

        string localId = qualifiedItemId[ItemRegistry.type_object.Length..];
        if (!DataLoader.Fish(Game1.content).TryGetValue(localId, out string? rawData))
            return null;

        // only places the player has been: a spot they've never seen can neither be recognised nor compared
        string[] places = this.Spawns.GetSpawns(qualifiedItemId)
            .Where(spawn => hasVisited(spawn.LocationName))
            .Select(spawn => spawn.LocationName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Season[] seasons = this.Spawns.GetSpawns(qualifiedItemId)
            .Where(spawn => hasVisited(spawn.LocationName))
            .SelectMany(spawn => spawn.Seasons)
            .Distinct()
            .OrderBy(season => season)
            .ToArray();

        string[] fields = rawData.Split('/');

        // crab pot fish: Name/trap/chance/.../waterType/...
        if (fields.Length > 4 && fields[1] == "trap")
            return new FishProfile(qualifiedItemId, fields[0], AllSeasons, "both", Array.Empty<(int, int)>(), places, Behaviour: "trap", Difficulty: 0, MinLevel: 0, IsCrabPot: true);

        // rod fish: Name/difficulty/behaviour/minSize/maxSize/times/seasons/weather/.../minFishingLevel/...
        if (fields.Length < 13)
            return null;

        return new FishProfile(
            Id: qualifiedItemId,
            InternalName: fields[0],
            Seasons: seasons.Length > 0 ? seasons : AllSeasons,
            Weather: fields[7],
            Times: ParseTimeRanges(fields[5]),
            Places: places,
            Behaviour: fields[2],
            Difficulty: int.TryParse(fields[1], out int difficulty) ? difficulty : 0,
            MinLevel: int.TryParse(fields[12], out int level) ? level : 0,
            IsCrabPot: false
        );
    }

    /// <summary>Get the phrase to use when a condition is simply not a condition, or <c>null</c> to compare instead.</summary>
    private static string? GetPlainKey(string axis, FishProfile fish)
    {
        if (fish.IsCrabPot && axis is "weather" or "time" or "level" or "fight")
            return axis == "weather" ? "crab-pot" : "not-applicable";

        return axis switch
        {
            "season" when fish.Seasons.Count >= 4 => "any-season",
            "weather" when fish.Weather == "both" => "any-weather",
            "time" when IsAllDay(fish.Times) => "any-time",
            "level" when fish.MinLevel <= 0 => "no-level",
            _ => null
        };
    }

    /// <summary>Get whether a fish bites at any hour.</summary>
    private static bool IsAllDay(IReadOnlyList<(int Start, int End)> times)
    {
        return times.Count == 0 || (times.Count == 1 && times[0].Start <= 600 && times[0].End >= 2600);
    }

    /// <summary>Get whether two fish share a condition exactly.</summary>
    private static bool Matches(string axis, FishProfile target, FishProfile other)
    {
        return axis switch
        {
            "season" => target.Seasons.OrderBy(s => s).SequenceEqual(other.Seasons.OrderBy(s => s)),
            "weather" => target.Weather == other.Weather,
            "time" => target.Times.SequenceEqual(other.Times),
            "place" => target.Places.Count > 0 && target.Places.Intersect(other.Places, StringComparer.OrdinalIgnoreCase).Any(),
            "level" => target.MinLevel == other.MinLevel,
            "fight" => target.Behaviour == other.Behaviour && Math.Abs(target.Difficulty - other.Difficulty) <= FightTolerance,
            _ => false
        };
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
