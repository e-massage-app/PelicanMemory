using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.Minimap;

/// <summary>Shows a zoomable minimap of the current location in the top-left corner, with the villagers the player has met.</summary>
/// <remarks>
/// Anti-spoil: only the current location is drawn (the player is standing in it), and only villagers already met
/// (<see cref="Farmer.friendshipData"/>) get a marker.
/// </remarks>
internal class MinimapFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The radius of the whole minimap, frame included, in pixels.</summary>
    private const int Radius = 100;

    /// <summary>The thickness of the round frame, in pixels.</summary>
    private const int RingWidth = 6;

    /// <summary>The distance between the minimap and the screen corner.</summary>
    private const int ScreenMargin = 20;

    /// <summary>The frame colour, matching the game's wooden UI.</summary>
    private static readonly Color RingColor = new(94, 52, 23);

    /// <summary>The darker line just inside the frame.</summary>
    private static readonly Color RingShadowColor = new(50, 28, 13);

    /// <summary>The vanilla minus and plus button sprites in <see cref="Game1.mouseCursors"/>.</summary>
    private static readonly Rectangle MinusSource = new(177, 345, 7, 8);
    private static readonly Rectangle PlusSource = new(184, 345, 7, 8);

    /// <summary>The scale applied to the zoom buttons.</summary>
    private const int ButtonScale = 4;

    /// <summary>The zoom buttons on screen, updated each time they're drawn.</summary>
    private Rectangle ZoomOutButton;
    private Rectangle ZoomInButton;

    /// <summary>The available zoom levels, as the scale applied to the rendered map.</summary>
    private static readonly float[] ZoomLevels = { 0.5f, 1f, 2f, 3f };

    private readonly MinimapRenderer Renderer = new();

    /// <summary>The index in <see cref="ZoomLevels"/>.</summary>
    private int ZoomIndex = 1;

    /// <summary>The config key for the minimap opacity, as a percentage.</summary>
    private const string OpacityKey = "minimap.opacity";

    /// <summary>The lowest opacity the slider can reach, so the minimap never disappears completely.</summary>
    private const int MinOpacity = 15;

    /// <summary>The opacity applied to everything the minimap draws, between 0 and 1.</summary>
    private float Opacity => Math.Clamp(this.Settings.GetNumber(OpacityKey, 100), MinOpacity, 100) / 100f;


    /*********
    ** Public methods
    *********/
    public override string Id => "minimap";

    public MinimapFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }

    /// <inheritdoc />
    public override IEnumerable<OptionsElement> CreateOptionRows()
    {
        yield return new FeatureSlider(
            label: this.Helper.Translation.Get("option.minimap-opacity"),
            description: this.Helper.Translation.Get("option.minimap-opacity.description"),
            value: this.Settings.GetNumber(OpacityKey, 100),
            onChanged: value => this.Settings.SetNumber(OpacityKey, value),
            x: 64
        );
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        this.Helper.Events.Display.RenderedHud += this.OnRenderedHud;
        this.Helper.Events.Player.Warped += this.OnWarped;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.UpdateTicked -= this.OnUpdateTicked;
        this.Helper.Events.Display.RenderedHud -= this.OnRenderedHud;
        this.Helper.Events.Player.Warped -= this.OnWarped;
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Helper.Events.GameLoop.ReturnedToTitle -= this.OnReturnedToTitle;
        this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed;

        this.Renderer.Dispose();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Rebuild the map texture if needed. This runs in the update phase, since rendering to a texture can't happen while the game is drawing.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.currentLocation?.Map is null || Game1.game1.takingMapScreenshot)
            return;

        try
        {
            GameLocation location = Game1.currentLocation;
            Point playerTile = Game1.player.TilePoint;
            if (this.Renderer.NeedsRender(location, playerTile))
                this.Renderer.Render(location, playerTile);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to render the minimap:\n{ex}", LogLevel.Error);
            this.Renderer.Dispose();
        }
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!this.ShouldDraw() || this.Renderer.Texture is not Texture2D map || map.IsDisposed)
            return;

        try
        {
            SpriteBatch b = e.SpriteBatch;
            float zoom = ZoomLevels[this.ZoomIndex];
            Point origin = this.Renderer.Origin;

            // the map is laid out in the square containing the circle, then drawn row by row within it
            int contentRadius = Radius - RingWidth;
            Rectangle area = new(ScreenMargin + Radius - contentRadius, ScreenMargin + Radius - contentRadius, contentRadius * 2, contentRadius * 2);
            Point center = new(ScreenMargin + Radius, ScreenMargin + Radius);

            MinimapGeometry.View? view = MinimapGeometry.GetView(MinimapGeometry.ToMapPixel(Game1.player.Position, origin), map.Bounds, area, zoom);
            if (view is null)
                return;

            float opacity = this.Opacity;
            this.DrawDisc(b, map, view, center, contentRadius, zoom, opacity);
            this.DrawMarkers(b, view, origin, center, contentRadius, zoom, opacity);
            DrawRing(b, center, contentRadius, opacity);
            this.DrawZoomButtons(b, center, contentRadius, opacity);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the minimap:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw the map as a disc, one row of pixels at a time.</summary>
    private void DrawDisc(SpriteBatch b, Texture2D map, MinimapGeometry.View view, Point center, int contentRadius, float zoom, float opacity)
    {
        for (int y = center.Y - contentRadius; y <= center.Y + contentRadius; y++)
        {
            int halfWidth = MinimapGeometry.GetCircleHalfWidth(y - center.Y, contentRadius);
            if (halfWidth <= 0)
                continue;

            // backdrop, so the disc is a solid shape even where the map doesn't reach
            b.Draw(Game1.staminaRect, new Rectangle(center.X - halfWidth, y, halfWidth * 2, 1), Color.Black * (0.55f * opacity));

            // the map row, clipped to both the disc and the drawn map
            int left = Math.Max(center.X - halfWidth, view.Target.Left);
            int right = Math.Min(center.X + halfWidth, view.Target.Right);
            if (right <= left || y < view.Target.Top || y >= view.Target.Bottom)
                continue;

            int sourceX = view.Visible.X + (int)((left - view.Target.X) / zoom);
            int sourceY = view.Visible.Y + (int)((y - view.Target.Y) / zoom);
            int sourceWidth = Math.Min(Math.Max(1, (int)Math.Ceiling((right - left) / zoom)), map.Width - sourceX);
            int sourceHeight = Math.Min(Math.Max(1, (int)Math.Ceiling(1 / zoom)), map.Height - sourceY);
            if (sourceWidth <= 0 || sourceHeight <= 0)
                continue;

            b.Draw(map, new Rectangle(left, y, right - left, 1), new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight), Color.White * opacity);
        }
    }

    /// <summary>Draw the villagers the player has met, then the player.</summary>
    private void DrawMarkers(SpriteBatch b, MinimapGeometry.View view, Point origin, Point center, int contentRadius, float zoom, float opacity)
    {
        float markerScale = Math.Max(1f, zoom);
        float markerSize = 16 * markerScale;
        Vector2 discCenter = new(center.X, center.Y);

        foreach (NPC npc in Game1.currentLocation.characters)
        {
            if (!npc.IsVillager || !Game1.player.friendshipData.ContainsKey(npc.Name))
                continue; // never reveal a villager the player hasn't met

            if (this.TryGetMarker(npc.Position, origin, view, zoom, markerSize, discCenter, contentRadius, out Vector2 position))
                b.Draw(npc.Sprite.Texture, position, npc.getMugShotSourceRect(), Color.White * opacity, 0f, Vector2.Zero, markerScale, SpriteEffects.None, 1f);
        }

        if (this.TryGetMarker(Game1.player.Position, origin, view, zoom, markerSize, discCenter, contentRadius, out Vector2 playerPosition))
            Game1.player.FarmerRenderer.drawMiniPortrat(b, playerPosition, 1f, markerScale, Game1.player.FacingDirection, Game1.player, opacity);
    }

    /// <summary>Get where to draw a marker, if it's both on the visible map and inside the disc.</summary>
    private bool TryGetMarker(Vector2 worldPosition, Point origin, MinimapGeometry.View view, float zoom, float markerSize, Vector2 discCenter, int contentRadius, out Vector2 position)
    {
        if (!MinimapGeometry.TryGetMarkerPosition(worldPosition, origin, view, zoom, markerSize, out position))
            return false;

        Vector2 markerCenter = position + new Vector2(markerSize / 2);
        return MinimapGeometry.IsInsideCircle(markerCenter, discCenter, contentRadius, markerSize);
    }

    /// <summary>Draw the zoom buttons under the minimap.</summary>
    private void DrawZoomButtons(SpriteBatch b, Point center, int contentRadius, float opacity)
    {
        int width = MinusSource.Width * ButtonScale;
        int height = MinusSource.Height * ButtonScale;
        int y = center.Y + contentRadius + RingWidth + 8;

        this.ZoomOutButton = new Rectangle(center.X - width - 6, y, width, height);
        this.ZoomInButton = new Rectangle(center.X + 6, y, width, height);

        b.Draw(Game1.mouseCursors, this.ZoomOutButton, MinusSource, Color.White * opacity * (this.ZoomIndex > 0 ? 1f : 0.4f));
        b.Draw(Game1.mouseCursors, this.ZoomInButton, PlusSource, Color.White * opacity * (this.ZoomIndex < ZoomLevels.Length - 1 ? 1f : 0.4f));
    }

    /// <summary>Draw the round wooden frame around the disc.</summary>
    private static void DrawRing(SpriteBatch b, Point center, int contentRadius, float opacity)
    {
        int outerRadius = contentRadius + RingWidth;

        for (int y = center.Y - outerRadius; y <= center.Y + outerRadius; y++)
        {
            int offsetY = y - center.Y;
            int outer = MinimapGeometry.GetCircleHalfWidth(offsetY, outerRadius);
            if (outer <= 0)
                continue;

            int inner = MinimapGeometry.GetCircleHalfWidth(offsetY, contentRadius);
            if (inner <= 0)
            {
                b.Draw(Game1.staminaRect, new Rectangle(center.X - outer, y, outer * 2, 1), RingColor * opacity);
                continue;
            }

            b.Draw(Game1.staminaRect, new Rectangle(center.X - outer, y, outer - inner, 1), RingColor * opacity);
            b.Draw(Game1.staminaRect, new Rectangle(center.X + inner, y, outer - inner, 1), RingColor * opacity);

            // a darker line hugging the map, like the inner edge of a vanilla frame
            b.Draw(Game1.staminaRect, new Rectangle(center.X - inner, y, 2, 1), RingShadowColor * opacity);
            b.Draw(Game1.staminaRect, new Rectangle(center.X + inner - 2, y, 2, 1), RingShadowColor * opacity);
        }
    }

    /// <summary>Get whether the minimap should be visible right now.</summary>
    private bool ShouldDraw()
    {
        return Context.IsWorldReady
            && Game1.currentLocation?.Map != null
            && Game1.displayHUD
            && Game1.activeClickableMenu is null
            && Game1.currentMinigame is null
            && !Game1.eventUp
            && Game1.farmEvent is null
            && !Game1.game1.takingMapScreenshot;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsPlayerFree || !this.ShouldDraw())
            return;

        int direction = 0;

        // keyboard
        if (e.Button == SButton.PageUp)
            direction = 1;
        else if (e.Button == SButton.PageDown)
            direction = -1;

        // zoom buttons
        else if (e.Button == SButton.MouseLeft)
        {
            Vector2 cursor = e.Cursor.GetScaledScreenPixels();
            Point point = new((int)cursor.X, (int)cursor.Y);
            if (this.ZoomInButton.Contains(point))
                direction = 1;
            else if (this.ZoomOutButton.Contains(point))
                direction = -1;
            else
                return;
        }

        if (direction == 0)
            return;

        int index = Math.Clamp(this.ZoomIndex + direction, 0, ZoomLevels.Length - 1);
        if (index != this.ZoomIndex)
        {
            this.ZoomIndex = index;
            Game1.playSound("smallSelect");
        }
        this.Helper.Input.Suppress(e.Button);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer)
            this.Renderer.Invalidate();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Renderer.Invalidate(); // the map can change overnight (season, buildings, upgrades)
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.Renderer.Dispose();
    }
}
