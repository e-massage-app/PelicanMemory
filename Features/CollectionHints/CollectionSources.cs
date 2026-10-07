using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace PelicanMemory.Features.CollectionHints;

/// <summary>One way to get a museum item.</summary>
/// <param name="Kind">The kind of source: geode, mineNode, artifactSpot, fishingTreasure, panning, monster, fishPond, shop or special.</param>
/// <param name="Subkind">For special sources, which one (mineBarrel, garbageCan, mail...).</param>
/// <param name="Detail">What the source is: a geode's ID, a location or monster name, or a description.</param>
/// <param name="Requires">What the player must have met for the source to be named, as in the data file.</param>
internal record CollectionSource(string Kind, string? Subkind, string Detail, JsonElement Requires);

/// <summary>Every way to get each museum mineral and artifact, and what each geode can give.</summary>
/// <remarks>
/// Read once from the data carried in the mod (<c>Data/collection-sources.json</c>), which was worked out from the game's
/// own code and data (1.6.15): geode drops, artifact spots, monster drops, mine nodes, fishing treasure, panning...
/// </remarks>
internal static class CollectionSources
{
    /*********
    ** Fields
    *********/
    private static Dictionary<string, List<CollectionSource>>? Items;
    private static Dictionary<string, HashSet<string>>? Geodes;


    /*********
    ** Public methods
    *********/
    /// <summary>Get the sources of a museum item, by unqualified object ID.</summary>
    public static IReadOnlyList<CollectionSource> GetSources(string itemId)
    {
        Load();
        return Items!.TryGetValue(itemId, out List<CollectionSource>? sources) ? sources : Array.Empty<CollectionSource>();
    }

    /// <summary>Get the museum items a geode can give (unqualified object IDs), or <c>null</c> if it isn't a geode the data knows.</summary>
    public static IReadOnlySet<string>? GetGeodeItems(string qualifiedGeodeId)
    {
        Load();
        return Geodes!.TryGetValue(qualifiedGeodeId, out HashSet<string>? items) ? items : null;
    }


    /*********
    ** Private methods
    *********/
    private static void Load()
    {
        if (Items != null)
            return;

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PelicanMemory.CollectionSources")
            ?? throw new InvalidOperationException("The collection sources data is missing from the mod.");
        using JsonDocument document = JsonDocument.Parse(stream);

        Dictionary<string, List<CollectionSource>> items = new();
        foreach (JsonProperty item in document.RootElement.GetProperty("items").EnumerateObject())
        {
            items[item.Name] = item.Value.EnumerateArray()
                .Select(source => new CollectionSource(
                    Kind: source.GetProperty("kind").GetString() ?? "",
                    Subkind: source.TryGetProperty("subkind", out JsonElement subkind) && subkind.ValueKind == JsonValueKind.String ? subkind.GetString() : null,
                    Detail: source.TryGetProperty("detail", out JsonElement detail) ? detail.GetString() ?? "" : "",
                    Requires: source.GetProperty("requires").Clone()
                ))
                .ToList();
        }

        Dictionary<string, HashSet<string>> geodes = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty geode in document.RootElement.GetProperty("geodes").EnumerateObject())
            geodes[geode.Name] = geode.Value.EnumerateArray().Select(id => id.GetString() ?? "").ToHashSet();

        Geodes = geodes;
        Items = items;
    }
}
