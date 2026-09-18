using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.WorldMaps;

namespace PelicanMemory.Core;

/// <summary>Links the world map data to the locations a player has actually visited.</summary>
internal static class WorldMapLookup
{
    /// <summary>Get the name recorded in <see cref="Farmer.locationsVisited"/> for a name used in <c>Data/WorldMap</c>.</summary>
    /// <remarks>Reverses the mapping done by <c>MapRegion.GetLocationName</c>, which renames the <c>Mine</c> location to <c>Mines</c>.</remarks>
    public static string GetVisitedName(string worldMapName)
    {
        return worldMapName.Equals("Mines", StringComparison.OrdinalIgnoreCase) ? "Mine" : worldMapName;
    }

    /// <summary>Get the location names matched by a world map position.</summary>
    public static IEnumerable<string> GetLocationNames(MapAreaPosition position)
    {
        if (position.Data.LocationName != null)
            yield return GetVisitedName(position.Data.LocationName);

        if (position.Data.LocationNames != null)
        {
            foreach (string name in position.Data.LocationNames)
                yield return GetVisitedName(name);
        }
    }

    /// <summary>Get the location names covered by a map area.</summary>
    public static IEnumerable<string> GetLocationNames(MapArea area)
    {
        return area.GetWorldPositions().SelectMany(GetLocationNames);
    }

    /// <summary>Get whether the player has been anywhere in a map area.</summary>
    /// <remarks>
    /// Some areas match a whole location context instead of named locations. The game shows those based on its own
    /// conditions, which can be met by another player in multiplayer, so we check the local player's visits instead.
    /// </remarks>
    public static bool HasVisited(Farmer player, MapArea area)
    {
        foreach (MapAreaPosition position in area.GetWorldPositions())
        {
            if (GetLocationNames(position).Any(player.locationsVisited.Contains))
                return true;

            string? contextId = position.Data.LocationContext;
            if (contextId != null && HasVisitedContext(player, contextId))
                return true;
        }

        return false;
    }

    /// <summary>Get whether the player has been to any location in a location context.</summary>
    private static bool HasVisitedContext(Farmer player, string contextId)
    {
        foreach (string locationName in player.locationsVisited)
        {
            if (Game1.getLocationFromName(locationName)?.GetLocationContextId() == contextId)
                return true;
        }

        return false;
    }

    /// <summary>Get the name of the map area containing a location, if the player has been somewhere in that area.</summary>
    /// <remarks>Used to place someone without naming a building the player has never entered, e.g. "Mountains" instead of "Carpenter's Shop".</remarks>
    public static bool TryGetKnownAreaName(GameLocation location, Farmer player, out string name)
    {
        name = "";

        MapAreaPositionWithContext? position = WorldMapManager.GetPositionData(location, Point.Zero);
        if (position is null)
            return false;

        MapArea area = position.Value.Data.Area;
        if (!HasVisited(player, area))
            return false;

        string? scrollText = area.GetScrollText();
        if (string.IsNullOrWhiteSpace(scrollText))
            return false;

        name = scrollText;
        return true;
    }
}
