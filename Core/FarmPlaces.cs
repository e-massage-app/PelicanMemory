using StardewValley;
using StardewValley.Locations;

namespace PelicanMemory.Core;

/// <summary>Where "the farm" ends, for the features which only make sense there.</summary>
/// <remarks>Shared so that putting away, moving objects and crafting from chests all agree on what counts as the farm.</remarks>
internal static class FarmPlaces
{
    /// <summary>Get whether a location is the farm or one of its buildings (the house, cabins, sheds, barns, the greenhouse…).</summary>
    /// <remarks>
    /// Uses the game's own notion of a farm location rather than the parent link, which the house, the greenhouse and
    /// building interiors don't always carry: relying on it left the house fridge and chests out entirely. Ginger
    /// Island has a farm of its own, but it's another place, so it's left out.
    /// </remarks>
    public static bool IsOnFarm(GameLocation? location)
    {
        if (location is null || location.InIslandContext())
            return false;

        return location.IsFarm
            || location.IsGreenhouse
            || location is Cellar
            || location.ParentBuilding?.GetParentLocation() is Farm;
    }
}
