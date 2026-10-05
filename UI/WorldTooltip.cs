using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>A tooltip for something in the world, like a crop or a machine.</summary>
/// <param name="Title">The bold first line, usually the thing's name.</param>
/// <param name="Body">The lines under it.</param>
internal record WorldTip(string Title, string Body);

/// <summary>Shows the game's own tooltip next to the cursor when it rests on something a feature describes.</summary>
/// <remarks>
/// Nothing else is drawn on screen: the tooltip only exists while the cursor is on the thing, like the game's
/// tooltips in its menus. Drawn with the HUD rather than the world, because the tooltip places itself from the mouse
/// in screen coordinates, which don't follow the world's zoom.
/// </remarks>
internal static class WorldTooltip
{
    /*********
    ** Fields
    *********/
    /// <summary>The features which may describe the tile under the cursor, asked in order until one answers.</summary>
    private static readonly List<Func<GameLocation, Vector2, WorldTip?>> Providers = new();

    private static IMonitor Monitor = null!;

    /// <summary>Whether another feature is using the cursor for its own tooltip, like an object being moved.</summary>
    public static bool Paused { get; set; }


    /*********
    ** Public methods
    *********/
    /// <summary>Start drawing tooltips for the registered providers.</summary>
    public static void Attach(IModEvents events, IMonitor monitor)
    {
        Monitor = monitor;
        events.Display.RenderedHud += OnRenderedHud;
    }

    /// <summary>Let a feature describe the tile under the cursor.</summary>
    /// <param name="provider">Get the tooltip for a tile of a location, or <c>null</c> if there's nothing to say.</param>
    public static void AddProvider(Func<GameLocation, Vector2, WorldTip?> provider)
    {
        if (!Providers.Contains(provider))
            Providers.Add(provider);
    }

    /// <summary>Stop asking a feature about the tile under the cursor.</summary>
    public static void RemoveProvider(Func<GameLocation, Vector2, WorldTip?> provider)
    {
        Providers.Remove(provider);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the tooltip for the tile under the cursor, if a feature has something to say about it.</summary>
    private static void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (Providers.Count == 0 || Paused || !Context.IsPlayerFree || Game1.eventUp || Game1.currentLocation is not GameLocation location)
            return;

        try
        {
            Vector2 tile = Game1.currentCursorTile;
            foreach (Func<GameLocation, Vector2, WorldTip?> provider in Providers)
            {
                if (provider(location, tile) is WorldTip tip)
                {
                    IClickableMenu.drawHoverText(e.SpriteBatch, tip.Body, Game1.smallFont, boldTitleText: tip.Title);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Monitor.LogOnce($"Failed to draw a tooltip in the world:\n{ex}", LogLevel.Error);
        }
    }
}
