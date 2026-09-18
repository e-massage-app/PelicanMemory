using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using xTile;
using xTile.Layers;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using XRectangle = xTile.Dimensions.Rectangle;

namespace PelicanMemory.Features.Minimap;

/// <summary>Renders a location's map into a texture, so the minimap only has to draw a scaled crop each frame.</summary>
/// <remarks>
/// The map is drawn at 16 pixels per tile (the game itself draws at 64). Very large maps are rendered as a window
/// around the player, re-rendered when the player gets close to its edge.
/// </remarks>
internal class MinimapRenderer : IDisposable
{
    /*********
    ** Fields
    *********/
    /// <summary>The number of pixels per tile in the rendered texture.</summary>
    public const int PixelsPerTile = 16;

    /// <summary>The largest window rendered at once, in tiles (4096px, safe for any graphics card).</summary>
    private const int MaxTiles = 256;

    /// <summary>How close to the window edge the player can get (in tiles) before it's re-rendered.</summary>
    private const int EdgeMargin = 32;

    private RenderTarget2D? Target;

    /// <summary>The sprite batch used to render the map, separate from the game's own batch which may be in use.</summary>
    private SpriteBatch? Batch;

    /// <summary>The name of the location in <see cref="Target"/>.</summary>
    private string? RenderedLocation;


    /*********
    ** Accessors
    *********/
    /// <summary>The rendered texture, if any.</summary>
    public Texture2D? Texture => this.Target;

    /// <summary>The top-left tile of the rendered window within the location.</summary>
    public Point Origin { get; private set; }


    /*********
    ** Public methods
    *********/
    /// <summary>Get whether the texture must be rebuilt for the given location and player position.</summary>
    public bool NeedsRender(GameLocation location, Point playerTile)
    {
        if (this.Target is null || this.Target.IsDisposed || this.RenderedLocation != location.NameOrUniqueName)
            return true;

        // the player is getting close to the edge of a windowed render
        Point size = this.GetRenderedSize();
        Point mapSize = GetMapSize(location.Map);
        bool windowed = size.X < mapSize.X || size.Y < mapSize.Y;
        if (!windowed)
            return false;

        return playerTile.X < this.Origin.X + EdgeMargin && this.Origin.X > 0
            || playerTile.Y < this.Origin.Y + EdgeMargin && this.Origin.Y > 0
            || playerTile.X > this.Origin.X + size.X - EdgeMargin && this.Origin.X + size.X < mapSize.X
            || playerTile.Y > this.Origin.Y + size.Y - EdgeMargin && this.Origin.Y + size.Y < mapSize.Y;
    }

    /// <summary>Render the location's map layers into the texture. Must be called outside the draw phase (no sprite batch may be active).</summary>
    public void Render(GameLocation location, Point playerTile)
    {
        Map map = location.Map;
        Point mapSize = GetMapSize(map);
        Point size = new(Math.Min(mapSize.X, MaxTiles), Math.Min(mapSize.Y, MaxTiles));
        if (size.X <= 0 || size.Y <= 0)
            return;

        // center the window on the player, within the map
        this.Origin = new Point(
            Math.Clamp(playerTile.X - size.X / 2, 0, Math.Max(0, mapSize.X - size.X)),
            Math.Clamp(playerTile.Y - size.Y / 2, 0, Math.Max(0, mapSize.Y - size.Y))
        );

        GraphicsDevice device = Game1.graphics.GraphicsDevice;
        if (this.Target is null || this.Target.IsDisposed || this.GetRenderedSize() != size)
        {
            this.Target?.Dispose();
            this.Target = new RenderTarget2D(device, size.X * PixelsPerTile, size.Y * PixelsPerTile);
        }

        this.Batch ??= new SpriteBatch(device);

        RenderTargetBinding[] previousTargets = device.GetRenderTargets();
        SpriteBatch b = this.Batch;
        XRectangle viewport = new(new xTile.Dimensions.Location(this.Origin.X * PixelsPerTile, this.Origin.Y * PixelsPerTile), new xTile.Dimensions.Size(this.Target.Width, this.Target.Height));

        try
        {
            device.SetRenderTarget(this.Target);
            device.Clear(Color.Transparent);
            Game1.mapDisplayDevice.BeginScene(b);

            // one batch per layer, like the game does, so layers keep their order
            DrawLayers(b, location.backgroundLayers, viewport);
            DrawLayers(b, location.buildingLayers, viewport);
            DrawLayers(b, location.frontLayers, viewport);
            DrawLayers(b, location.alwaysFrontLayers, viewport);

            Game1.mapDisplayDevice.EndScene();
            this.RenderedLocation = location.NameOrUniqueName;
        }
        finally
        {
            // hand the game's own batch back to the display device
            Game1.mapDisplayDevice.BeginScene(Game1.spriteBatch);

            // an empty array means the game was drawing straight to the back buffer
            if (previousTargets.Length > 0)
                device.SetRenderTargets(previousTargets);
            else
                device.SetRenderTarget(null);
        }
    }

    /// <summary>Forget the rendered texture, so it's rebuilt on the next render.</summary>
    public void Invalidate()
    {
        this.RenderedLocation = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Target?.Dispose();
        this.Target = null;
        this.Batch?.Dispose();
        this.Batch = null;
        this.RenderedLocation = null;
    }


    /*********
    ** Private methods
    *********/
    private static void DrawLayers(SpriteBatch b, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Layer, int>> layers, XRectangle viewport)
    {
        foreach ((Layer layer, int _) in layers)
        {
            b.Begin(SpriteSortMode.Texture, BlendState.AlphaBlend, SamplerState.PointClamp);
            try
            {
                layer.Draw(Game1.mapDisplayDevice, viewport, xTile.Dimensions.Location.Origin, wrapAround: false, PixelsPerTile / 16, -1f);
            }
            finally
            {
                b.End();
            }
        }
    }

    /// <summary>Get the rendered window size in tiles.</summary>
    private Point GetRenderedSize()
    {
        return this.Target is null || this.Target.IsDisposed
            ? Point.Zero
            : new Point(this.Target.Width / PixelsPerTile, this.Target.Height / PixelsPerTile);
    }

    /// <summary>Get a map's size in tiles.</summary>
    private static Point GetMapSize(Map map)
    {
        Point size = Point.Zero;
        foreach (Layer layer in map.Layers)
        {
            size.X = Math.Max(size.X, layer.LayerWidth);
            size.Y = Math.Max(size.Y, layer.LayerHeight);
        }
        return size;
    }
}
