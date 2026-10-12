using StardewModdingAPI;
using StardewValley;

namespace PelicanMemory.Core;

/// <summary>Gets a place's name as the player should read it.</summary>
/// <remarks>
/// Most places have a translated name in the game's data, but a few (the mutant bug lair, the witch's swamp) have
/// none, and the game would show their internal name. Those come from the mod's own translations instead.
/// </remarks>
internal static class PlaceNames
{
    /// <summary>Get the display name of a location from its internal name.</summary>
    public static string Get(string locationName, ITranslationHelper translations)
    {
        GameLocation? location = Game1.getLocationFromName(locationName);
        string? fromData = location?.GetDisplayName();
        if (!string.IsNullOrWhiteSpace(fromData))
            return fromData;

        Translation fromMod = translations.Get($"location.{locationName}");
        if (fromMod.HasValue())
            return fromMod;

        return location?.DisplayName ?? locationName;
    }
}
