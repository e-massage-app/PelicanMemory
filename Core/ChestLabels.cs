using StardewValley.Objects;

namespace PelicanMemory.Core;

/// <summary>The names the player gives their chests.</summary>
/// <remarks>
/// The game has no notion of a chest name, so the name is kept in the chest's own <see cref="StardewValley.Object.modData"/>:
/// it travels with the chest, is saved with the game, and is synced to the other player in multiplayer. A chest whose
/// name is removed simply goes back to being named after the place it stands in.
/// </remarks>
internal static class ChestLabels
{
    /// <summary>The mod data key holding the name.</summary>
    private const string DataKey = "JordanNeau.PelicanMemory/label";

    /// <summary>Get the name the player gave a chest, or <c>null</c> if they never named it.</summary>
    public static string? Get(Chest chest)
    {
        return chest.modData.TryGetValue(DataKey, out string? name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    /// <summary>Name a chest, or forget its name when given nothing.</summary>
    public static void Set(Chest chest, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            chest.modData.Remove(DataKey);
        else
            chest.modData[DataKey] = name.Trim();
    }
}
