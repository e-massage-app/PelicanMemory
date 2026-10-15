using System.Linq;
using StardewValley;

namespace PelicanMemory.Core;

/// <summary>Whether the player has been to places the game builds on the fly, which it never lists among the places visited.</summary>
/// <remarks>
/// <c>Game1.onWarp</c> skips <c>MineShaft.IsGeneratedLevel</c> and <c>VolcanoDungeon.IsGeneratedLevel</c> when it adds to
/// <see cref="Farmer.locationsVisited"/>: "VolcanoDungeon0" is never in it, however often the player went in.
/// </remarks>
internal static class GeneratedLevels
{
    /// <summary>The monsters only met in the volcano dungeon: beating one means having been there.</summary>
    private static readonly string[] VolcanoMonsters = { "Magma Sprite", "Magma Sparker", "Magma Duggy", "False Magma Cap", "Hot Head", "Lava Lurk", "Spiker", "Dwarvish Sentry" };

    /// <summary>Get whether the player has been in the volcano dungeon.</summary>
    /// <remarks>
    /// The game flags the player who comes near the flame sprite at the dungeon's entrance (<c>Saw_Flame_Sprite_Volcano</c>,
    /// <c>VolcanoDungeon.UpdateWhenCurrentLocation</c>); a monster beaten there covers a save from before the flag.
    /// </remarks>
    public static bool HasBeenInVolcano(Farmer player)
    {
        return player.hasOrWillReceiveMail("Saw_Flame_Sprite_Volcano")
            || VolcanoMonsters.Any(monster => Game1.stats.getMonstersKilled(monster) > 0);
    }
}
