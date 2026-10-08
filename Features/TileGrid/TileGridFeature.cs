using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PelicanMemory.Features.TileGrid;

/// <summary>A key which shows or hides the outline of every tile of the current location, to plan a layout.</summary>
/// <remarks>Only draws lines over the map the player is already standing in: nothing to spoil, nothing to assist.</remarks>
internal class TileGridFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The config key for the keybind.</summary>
    private const string KeybindKey = "tile-grid";

    /// <summary>The config key which remembers whether the grid is shown, so it survives between sessions.</summary>
    private const string VisibleKey = "tile-grid.visible";

    /// <summary>How dark the lines are: visible on grass and floors without hiding what's under them.</summary>
    private const float LineOpacity = 0.35f;

    private bool Visible
    {
        get => this.Settings.GetFlag(VisibleKey, false);
        set => this.Settings.SetFlag(VisibleKey, value);
    }


    /*********
    ** Public methods
    *********/
    public override string Id => "tile-grid";

    public TileGridFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
        this.Helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Display.RenderedWorld -= this.OnRenderedWorld;
        this.Helper.Events.Input.ButtonsChanged -= this.OnButtonsChanged;
    }


    /*********
    ** Private methods
    *********/
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsPlayerFree || !this.Settings.GetKeybind(KeybindKey, "G").JustPressed())
            return;

        this.Visible = !this.Visible;
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get(this.Visible ? "tile-grid.on" : "tile-grid.off"), HUDMessage.newQuest_type));
    }

    /// <summary>Draw one line along the top and one along the left of each visible tile, so every edge is drawn once.</summary>
    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!this.Visible || !Context.IsWorldReady || Game1.currentLocation?.Map is null || Game1.eventUp)
            return;

        try
        {
            var layer = Game1.currentLocation.Map.Layers[0];
            int mapWidth = layer.LayerWidth;
            int mapHeight = layer.LayerHeight;
            int size = Game1.tileSize;

            // the visible tiles, clamped to the map so the lines stop at its edges
            int x0 = Math.Max(0, Game1.viewport.X / size);
            int y0 = Math.Max(0, Game1.viewport.Y / size);
            int x1 = Math.Min(mapWidth, (Game1.viewport.X + Game1.viewport.Width) / size + 1);
            int y1 = Math.Min(mapHeight, (Game1.viewport.Y + Game1.viewport.Height) / size + 1);
            if (x1 <= x0 || y1 <= y0)
                return;

            Color color = Color.Black * LineOpacity;
            Vector2 topLeft = Game1.GlobalToLocal(Game1.viewport, new Vector2(x0 * size, y0 * size));
            int left = (int)topLeft.X;
            int top = (int)topLeft.Y;
            int width = (x1 - x0) * size;
            int height = (y1 - y0) * size;

            for (int x = 0; x <= x1 - x0; x++)
                e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(left + x * size, top, 1, height), color);
            for (int y = 0; y <= y1 - y0; y++)
                e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle(left, top + y * size, width, 1), color);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the tile grid:\n{ex}", LogLevel.Error);
            this.Visible = false;
        }
    }
}
