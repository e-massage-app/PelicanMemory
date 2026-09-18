using System;
using Microsoft.Xna.Framework;

namespace PelicanMemory.Features.Minimap;

/// <summary>The coordinate maths behind the minimap, kept separate so it can be checked without running the game.</summary>
/// <remarks>
/// Three coordinate spaces are involved:
/// <list type="bullet">
///   <item><b>world</b>: the game's pixels, 64 per tile;</item>
///   <item><b>map</b>: pixels in the rendered texture, 16 per tile, offset by the rendered window's origin;</item>
///   <item><b>screen</b>: where the minimap is drawn.</item>
/// </list>
/// </remarks>
internal static class MinimapGeometry
{
    /// <summary>The part of the rendered map which is visible, and where it's drawn on screen.</summary>
    /// <param name="Visible">The source area within the rendered texture.</param>
    /// <param name="Target">The screen area it's drawn into. Smaller than the minimap area when the map doesn't fill it.</param>
    internal record View(Rectangle Visible, Rectangle Target);

    /// <summary>Convert a world position into a pixel in the rendered texture.</summary>
    /// <param name="worldPosition">The position in world pixels.</param>
    /// <param name="renderOrigin">The top-left tile of the rendered window.</param>
    public static Vector2 ToMapPixel(Vector2 worldPosition, Point renderOrigin)
    {
        const int worldPixelsPerMapPixel = Game1TileSize / MinimapRenderer.PixelsPerTile;
        return new Vector2(
            worldPosition.X / worldPixelsPerMapPixel - renderOrigin.X * MinimapRenderer.PixelsPerTile,
            worldPosition.Y / worldPixelsPerMapPixel - renderOrigin.Y * MinimapRenderer.PixelsPerTile
        );
    }

    /// <summary>Get the visible part of the map centered on the player, and where to draw it.</summary>
    /// <param name="playerMapPixel">The player's position in the rendered texture.</param>
    /// <param name="mapBounds">The rendered texture's bounds.</param>
    /// <param name="area">The minimap area on screen.</param>
    /// <param name="zoom">The scale applied to the rendered map.</param>
    /// <returns>Returns the view, or <c>null</c> if nothing is visible.</returns>
    public static View? GetView(Vector2 playerMapPixel, Rectangle mapBounds, Rectangle area, float zoom)
    {
        int sourceWidth = (int)(area.Width / zoom);
        int sourceHeight = (int)(area.Height / zoom);
        Rectangle source = new((int)playerMapPixel.X - sourceWidth / 2, (int)playerMapPixel.Y - sourceHeight / 2, sourceWidth, sourceHeight);

        Rectangle visible = Rectangle.Intersect(source, mapBounds);
        if (visible.IsEmpty)
            return null;

        Rectangle target = new(
            area.X + (int)((visible.X - source.X) * zoom),
            area.Y + (int)((visible.Y - source.Y) * zoom),
            (int)(visible.Width * zoom),
            (int)(visible.Height * zoom)
        );
        return new View(visible, target);
    }

    /// <summary>Get the top-left screen position for a marker, if it's within the visible part of the map.</summary>
    /// <param name="worldPosition">The marker's position in world pixels.</param>
    /// <param name="renderOrigin">The top-left tile of the rendered window.</param>
    /// <param name="view">The current view.</param>
    /// <param name="zoom">The scale applied to the rendered map.</param>
    /// <param name="markerSize">The marker's drawn size in pixels, so it's centered on the position.</param>
    /// <param name="position">The position at which to draw the marker.</param>
    public static bool TryGetMarkerPosition(Vector2 worldPosition, Point renderOrigin, View view, float zoom, float markerSize, out Vector2 position)
    {
        Vector2 mapPixel = ToMapPixel(worldPosition, renderOrigin);
        if (!view.Visible.Contains((int)mapPixel.X, (int)mapPixel.Y))
        {
            position = Vector2.Zero;
            return false;
        }

        position = new Vector2(
            view.Target.X + (mapPixel.X - view.Visible.X) * zoom - markerSize / 2,
            view.Target.Y + (mapPixel.Y - view.Visible.Y) * zoom - markerSize / 2
        );
        return true;
    }

    /// <summary>Get half the width of a circle's row, or a negative value if the row is outside the circle.</summary>
    /// <param name="offsetY">The row's distance from the circle's centre.</param>
    /// <param name="radius">The circle's radius.</param>
    public static int GetCircleHalfWidth(int offsetY, int radius)
    {
        int squared = radius * radius - offsetY * offsetY;
        return squared < 0
            ? -1
            : (int)Math.Sqrt(squared);
    }

    /// <summary>Get whether a point is inside a circle, leaving room for something of the given size drawn on it.</summary>
    public static bool IsInsideCircle(Vector2 point, Vector2 center, float radius, float size)
    {
        return Vector2.Distance(point, center) <= radius - size / 2f;
    }

    /// <summary>The game's world pixels per tile (<c>Game1.tileSize</c>), repeated here so this class doesn't need the game running.</summary>
    private const int Game1TileSize = 64;
}
