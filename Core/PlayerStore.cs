using System.Text.Json;
using StardewValley;

namespace PelicanMemory.Core;

/// <summary>Stores per-player, per-save progression data for features that need their own tracking.</summary>
/// <remarks>
/// Data lives in <see cref="Farmer.modData"/>, which the vanilla game saves with the world and syncs in multiplayer.
/// Unlike SMAPI's <c>ReadSaveData</c>/<c>WriteSaveData</c> (host only), this works for farmhands too, and the other
/// players don't need the mod installed.
/// </remarks>
internal class PlayerStore
{
    private readonly string KeyPrefix;

    public PlayerStore(string modId)
    {
        this.KeyPrefix = modId + "/";
    }

    /// <summary>Read a value for a player, or <c>null</c> if none was saved.</summary>
    public T? Read<T>(Farmer player, string key) where T : class
    {
        return player.modData.TryGetValue(this.KeyPrefix + key, out string? json) && !string.IsNullOrEmpty(json)
            ? JsonSerializer.Deserialize<T>(json)
            : null;
    }

    /// <summary>Save a value for a player. Passing <c>null</c> removes it.</summary>
    public void Write<T>(Farmer player, string key, T? value) where T : class
    {
        if (value is null)
            player.modData.Remove(this.KeyPrefix + key);
        else
            player.modData[this.KeyPrefix + key] = JsonSerializer.Serialize(value);
    }
}
