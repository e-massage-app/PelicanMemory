using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.FarmLayers;

/// <summary>Which coverage is shaded on the ground.</summary>
[Flags]
internal enum FarmLayer
{
    None = 0,
    Sprinklers = 1,
    Scarecrows = 2,
    All = Sprinklers | Scarecrows
}

/// <summary>Shades what the player's sprinklers and scarecrows cover, automatically while placing one.</summary>
/// <remarks>
/// Everything here comes from the player's own machines, so there's nothing to spoil. Holding a sprinkler or a
/// scarecrow shows that layer by itself, plus a brighter preview of what the one in hand would cover: showing both
/// layers at once makes the colours overlap and hides exactly what you're trying to judge.
/// </remarks>
internal class FarmLayersFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The config key for the layer shown with the keybind, so it survives between sessions.</summary>
    private const string ModeKey = "farm-layers.mode";

    /// <summary>The config key for the keybind.</summary>
    private const string KeybindKey = "farm-layers";

    private static readonly Color SprinklerColor = new(70, 170, 255);
    private static readonly Color ScarecrowColor = new(255, 150, 40);

    /// <summary>How visible an existing machine's coverage is.</summary>
    private const float CoverageOpacity = 0.3f;

    /// <summary>How visible the coverage of the machine in hand is.</summary>
    private const float PreviewOpacity = 0.55f;

    /// <summary>The tiles covered by what's already placed in the current location.</summary>
    private readonly List<Vector2> SprinklerTiles = new();
    private readonly List<Vector2> ScarecrowTiles = new();

    /// <summary>Whether the cached tiles must be rebuilt.</summary>
    private bool IsDirty = true;

    /// <summary>The layer shown by the keybind.</summary>
    private FarmLayer Mode
    {
        get => (FarmLayer)this.Settings.GetNumber(ModeKey, (int)FarmLayer.None);
        set => this.Settings.SetNumber(ModeKey, (int)value);
    }


    /*********
    ** Public methods
    *********/
    public override string Id => "farm-layers";

    public FarmLayersFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
        this.Helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        this.Helper.Events.Player.Warped += this.OnWorldChanged;
        this.Helper.Events.World.ObjectListChanged += this.OnWorldChanged;
        this.Helper.Events.GameLoop.DayStarted += this.OnWorldChanged;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Display.RenderedWorld -= this.OnRenderedWorld;
        this.Helper.Events.Input.ButtonsChanged -= this.OnButtonsChanged;
        this.Helper.Events.Player.Warped -= this.OnWorldChanged;
        this.Helper.Events.World.ObjectListChanged -= this.OnWorldChanged;
        this.Helper.Events.GameLoop.DayStarted -= this.OnWorldChanged;

        this.SprinklerTiles.Clear();
        this.ScarecrowTiles.Clear();
        this.IsDirty = true;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Cycle the keybind through: nothing, sprinklers, scarecrows, both.</summary>
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsPlayerFree || !this.Settings.GetKeybind(KeybindKey, "F3").JustPressed())
            return;

        FarmLayer next = this.Mode switch
        {
            FarmLayer.None => FarmLayer.Sprinklers,
            FarmLayer.Sprinklers => FarmLayer.Scarecrows,
            FarmLayer.Scarecrows => FarmLayer.All,
            _ => FarmLayer.None
        };
        this.Mode = next;

        string label = next switch
        {
            FarmLayer.Sprinklers => "farm-layers.sprinklers",
            FarmLayer.Scarecrows => "farm-layers.scarecrows",
            FarmLayer.All => "farm-layers.all",
            _ => "farm-layers.off"
        };
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get(label), HUDMessage.newQuest_type));
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is null || Game1.eventUp)
            return;

        try
        {
            SObject? held = Game1.player.ActiveObject;
            bool holdingSprinkler = held?.IsSprinkler() == true;
            bool holdingScarecrow = held?.IsScarecrow() == true;

            // while placing one, that layer alone is shown, whatever the keybind says
            FarmLayer layers = this.Mode;
            if (holdingSprinkler)
                layers = FarmLayer.Sprinklers;
            else if (holdingScarecrow)
                layers = FarmLayer.Scarecrows;

            if (layers == FarmLayer.None)
                return;

            if (this.IsDirty)
                this.RebuildTiles(Game1.currentLocation);

            if (layers.HasFlag(FarmLayer.Sprinklers))
                this.DrawTiles(e.SpriteBatch, this.SprinklerTiles, SprinklerColor, CoverageOpacity);
            if (layers.HasFlag(FarmLayer.Scarecrows))
                this.DrawTiles(e.SpriteBatch, this.ScarecrowTiles, ScarecrowColor, CoverageOpacity);

            // what the one in hand would cover, brighter and drawn last
            if (holdingSprinkler || holdingScarecrow)
            {
                Vector2 tile = Game1.GetPlacementGrabTile();
                tile = new Vector2((int)tile.X, (int)tile.Y);

                List<Vector2> preview = holdingSprinkler
                    ? GetSprinklerCoverage(held!, tile)
                    : GetScarecrowCoverage(held!, tile);

                this.DrawTiles(e.SpriteBatch, preview, holdingSprinkler ? SprinklerColor : ScarecrowColor, PreviewOpacity);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the farm layers:\n{ex}", LogLevel.Error);
            this.Mode = FarmLayer.None;
        }
    }

    /// <summary>Shade a set of tiles, skipping those off screen.</summary>
    private void DrawTiles(SpriteBatch b, List<Vector2> tiles, Color color, float opacity)
    {
        foreach (Vector2 tile in tiles)
        {
            Vector2 position = Game1.GlobalToLocal(Game1.viewport, tile * Game1.tileSize);
            if (position.X < -Game1.tileSize || position.Y < -Game1.tileSize || position.X > Game1.viewport.Width || position.Y > Game1.viewport.Height)
                continue;

            b.Draw(Game1.staminaRect, new Rectangle((int)position.X + 2, (int)position.Y + 2, Game1.tileSize - 4, Game1.tileSize - 4), color * opacity);
        }
    }

    /// <summary>Collect the tiles covered by the machines already placed in a location.</summary>
    private void RebuildTiles(GameLocation location)
    {
        this.SprinklerTiles.Clear();
        this.ScarecrowTiles.Clear();
        this.IsDirty = false;

        foreach ((Vector2 tile, SObject obj) in location.Objects.Pairs)
        {
            if (obj.IsSprinkler())
                this.SprinklerTiles.AddRange(obj.GetSprinklerTiles());
            else if (obj.IsScarecrow())
                this.ScarecrowTiles.AddRange(GetScarecrowCoverage(obj, tile));
        }
    }

    /// <summary>Get the tiles a sprinkler would water from a tile.</summary>
    private static List<Vector2> GetSprinklerCoverage(SObject sprinkler, Vector2 tile)
    {
        // the sprinkler answers for its own position, so ask a copy placed on the target tile
        SObject copy = (SObject)sprinkler.getOne();
        copy.TileLocation = tile;
        return copy.GetSprinklerTiles();
    }

    /// <summary>Get the tiles a scarecrow protects from a tile, using the game's own rule.</summary>
    private static List<Vector2> GetScarecrowCoverage(SObject scarecrow, Vector2 tile)
    {
        List<Vector2> tiles = new();
        int radius = scarecrow.GetRadiusForScarecrow();

        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                Vector2 covered = new(tile.X + x, tile.Y + y);
                if (Vector2.Distance(tile, covered) < radius)
                    tiles.Add(covered);
            }
        }

        return tiles;
    }

    private void OnWorldChanged(object? sender, EventArgs e)
    {
        this.IsDirty = true;
    }
}
