using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PelicanMemory.Features.Bestiary;

/// <summary>A place where a monster lives.</summary>
/// <param name="Kind">
/// <c>mine</c> (floors <see cref="From"/>–<see cref="To"/> of the mines), <c>cavern</c> (Skull Cavern, from floor
/// <see cref="From"/>), <c>volcano</c> (the volcano dungeon), or <c>location</c> (the game locations in <see cref="Names"/>).
/// </param>
/// <param name="From">The first floor, for the mines and the cavern.</param>
/// <param name="To">The last floor, for the mines.</param>
/// <param name="Names">The game location names, for <c>location</c>: the place counts as known once one was visited.</param>
/// <param name="Note">A condition, as a translation key suffix (<c>bestiary.note.&lt;Note&gt;</c>): at night, infested floors...</param>
/// <param name="Best">Whether it's the place to go to beat many quickly.</param>
internal record MonsterPlace(string Kind, int From = 0, int To = 0, string[]? Names = null, string? Note = null, bool Best = false);

/// <summary>A monster of the bestiary.</summary>
/// <param name="Key">The name the game counts kills under (<c>Stats.getMonstersKilled</c>).</param>
/// <param name="Texture">The sprite sheet.</param>
/// <param name="Icon">The part of the sheet shown as its face: x, y, width, height.</param>
/// <param name="Tint">A colour the game tints the sprite with (slimes), as r, g, b; or none.</param>
/// <param name="Places">Where it lives.</param>
internal record MonsterEntry(string Key, string Texture, int[] Icon, int[]? Tint, MonsterPlace[] Places);

/// <summary>The bestiary's data: every monster, its face and where it lives.</summary>
/// <remarks>
/// Carried in the mod (<c>Data/bestiary.json</c>), worked out from the game's code (1.6.15): mine floors from
/// <c>MineShaft</c>, the cavern, the volcano, the island, the farm at night...
/// </remarks>
internal static class BestiaryData
{
    private static List<MonsterEntry>? Monsters;

    /// <summary>Get every monster, in the bestiary's order.</summary>
    public static IReadOnlyList<MonsterEntry> GetMonsters()
    {
        if (Monsters != null)
            return Monsters;

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PelicanMemory.Bestiary")
            ?? throw new InvalidOperationException("The bestiary data is missing from the mod.");
        BestiaryFile file = JsonSerializer.Deserialize<BestiaryFile>(stream, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidOperationException("The bestiary data is empty.");
        return Monsters = file.Monsters;
    }

    private class BestiaryFile
    {
        [JsonPropertyName("Monsters")]
        public List<MonsterEntry> Monsters { get; set; } = new();
    }
}
