using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.Buildings;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.MoveObjects;

/// <summary>Shift + click a chest, machine or decoration to pick it up, then click where it should go: it moves with everything inside.</summary>
/// <remarks>
/// The game only lets a full chest be knocked one tile at a time, and picking up a working machine throws its work
/// away. Here the very same object changes tile, the way the game itself moves a knocked chest: contents, progress
/// and settings stay, and nothing is ever in the player's hands, so nothing can be lost if the game stops in between:
/// the object keeps standing where it was until the very moment it's put down.
/// On the farm, the object can be taken from one building to another (a slime incubator into the hutch, chests into
/// the house): click the building itself and it goes in, on the free spot nearest the door; or walk in and put it
/// down by hand. Elsewhere, within the same place only. Things tied to their
/// tile stay put: tappers on their tree, crab pots in their water, garden pots with their plant; furniture is already
/// picked up by a right click.
/// </remarks>
internal class MoveObjectsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The config key for the pick-up keybind.</summary>
    private const string KeybindKey = "move-objects";

    /// <summary>The default pick-up keybind: either Shift with a left click.</summary>
    private const string DefaultKeybind = "LeftShift + MouseLeft, RightShift + MouseLeft";

    /// <summary>The object being moved, still standing on its tile until it's put down.</summary>
    private SObject? Carried;

    /// <summary>Where the carried object stands.</summary>
    private GameLocation? CarriedFrom;
    private Vector2 CarriedTile;


    /*********
    ** Public methods
    *********/
    public override string Id => "move-objects";

    public MoveObjectsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        this.Helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
        this.Helper.Events.Display.RenderedHud += this.OnRenderedHud;
        this.Helper.Events.Player.Warped += this.OnWarped;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;
        this.Helper.Events.GameLoop.DayEnding += this.OnDayEnding;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Input.ButtonsChanged -= this.OnButtonsChanged;
        this.Helper.Events.Display.RenderedWorld -= this.OnRenderedWorld;
        this.Helper.Events.Display.RenderedHud -= this.OnRenderedHud;
        this.Helper.Events.Player.Warped -= this.OnWarped;
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        this.Helper.Events.GameLoop.DayEnding -= this.OnDayEnding;
        this.Drop();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Pick up with Shift + click; then click to put down, or Escape to cancel. A right click keeps its usual job: opening doors.</summary>
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        try
        {
            if (this.Carried != null)
            {
                if (this.JustPressed(e, SButton.Escape))
                {
                    this.Helper.Input.Suppress(SButton.Escape);
                    this.Drop();
                    Game1.playSound("cancel");
                }
                else if (this.JustPressed(e, SButton.MouseLeft))
                {
                    this.Helper.Input.Suppress(SButton.MouseLeft);
                    if (this.GetTargetBuilding() is (Building building, GameLocation indoors))
                        this.PutInBuilding(building, indoors);
                    else
                        this.PutDown(Game1.currentCursorTile);
                }
                return;
            }

            KeybindList keybind = this.Settings.GetKeybind(KeybindKey, DefaultKeybind);
            if (!Context.IsPlayerFree || !keybind.JustPressed() || Game1.currentLocation is not GameLocation location)
                return;

            if (this.FindMovable(location, Game1.currentCursorTile, out Vector2 tile) is not SObject target)
                return;

            this.Helper.Input.SuppressActiveKeybinds(keybind);

            if (target is Chest chest && chest.GetMutex().IsLocked())
            {
                Game1.showRedMessage(this.T("move-objects.in-use"));
                return;
            }

            this.Carried = target;
            this.CarriedFrom = location;
            this.CarriedTile = tile;
            WorldTooltip.Paused = true;
            Game1.playSound("pickUpItem");
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to move an object:\n{ex}", LogLevel.Error);
            this.Drop();
        }
    }

    /// <summary>Get whether a button was pressed this tick.</summary>
    private bool JustPressed(ButtonsChangedEventArgs e, SButton button)
    {
        foreach (SButton pressed in e.Pressed)
        {
            if (pressed == button)
                return true;
        }
        return false;
    }

    /// <summary>Get the object which can be moved on a tile, or on the tile below for the top half of a tall object.</summary>
    private SObject? FindMovable(GameLocation location, Vector2 cursorTile, out Vector2 tile)
    {
        tile = cursorTile;
        if (location.objects.TryGetValue(cursorTile, out SObject? obj) && IsMovable(obj))
            return obj;

        tile = cursorTile + new Vector2(0, 1);
        if (location.objects.TryGetValue(tile, out obj) && obj.bigCraftable.Value && IsMovable(obj))
            return obj;

        return null;
    }

    /// <summary>Get whether an object is something the player placed and which can change tile without breaking.</summary>
    private static bool IsMovable(SObject obj)
    {
        // tied to their tile, or already movable by hand
        if (obj is Furniture or CrabPot or IndoorPot || obj.IsTapper())
            return false;

        // things the game itself refuses to let the player remove
        if (obj.Fragility == SObject.fragility_Indestructable)
            return false;

        // a chest only if it's the player's: the game's own chests, like treasure, stay where it put them
        if (obj is Chest chest)
            return chest.playerChest.Value;

        // only what the player made and placed: machines, signs, lamps, sprinklers... not stones, weeds or forage
        return obj.bigCraftable.Value || obj.Type == "Crafting";
    }

    /// <summary>Get whether the carried object can stand on a tile.</summary>
    private bool CanPlaceAt(GameLocation location, Vector2 tile)
    {
        // the same checks the game uses when it knocks a chest to the next tile
        return this.Carried != null
            && !location.objects.ContainsKey(tile)
            && this.Carried.canBePlacedHere(location, tile)
            && location.CanItemBePlacedHere(tile);
    }

    /// <summary>Put the carried object down on a tile of the current place.</summary>
    private void PutDown(Vector2 tile)
    {
        // clicking where it already stands: just let go
        if (tile == this.CarriedTile && ReferenceEquals(this.CarriedFrom, Game1.currentLocation))
        {
            this.Drop();
            Game1.playSound("cancel");
            return;
        }

        if (this.MoveTo(Game1.currentLocation, tile))
            Game1.playSound("woodyStep");
    }

    /// <summary>Put the carried object inside a building, on the free spot nearest its door.</summary>
    private void PutInBuilding(Building building, GameLocation indoors)
    {
        if (this.FindFreeSpot(indoors) is not Vector2 tile)
        {
            Game1.showRedMessage(this.Helper.Translation.Get("move-objects.no-room", new { place = indoors.DisplayName }));
            return;
        }

        string name = this.Carried?.DisplayName ?? "";
        if (this.MoveTo(indoors, tile))
        {
            Game1.playSound("woodyStep");
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("move-objects.put-in", new { item = name, place = indoors.DisplayName }), HUDMessage.achievement_type));
        }
    }

    /// <summary>Move the carried object to a tile of a place, in one step, if it can stand there.</summary>
    /// <returns>Whether it moved.</returns>
    private bool MoveTo(GameLocation destination, Vector2 tile)
    {
        SObject? obj = this.Carried;
        GameLocation? location = this.CarriedFrom;
        if (obj is null || location is null)
            return false;

        if (!this.CanCarryTo(destination))
        {
            this.Drop();
            return false;
        }

        // it may have changed meanwhile (another player, a hopper): only move it if it's still where it was
        if (!location.objects.TryGetValue(this.CarriedTile, out SObject? standing) || !ReferenceEquals(standing, obj))
        {
            this.Drop();
            return false;
        }

        if (!this.CanPlaceAt(destination, tile) || (obj is Chest chest && chest.GetMutex().IsLocked()))
        {
            Game1.playSound("cancel");
            return false;
        }

        // the light follows: remove it from the old tile, light it again on the new one
        bool hadLight = obj.lightSource != null;
        location.removeLightSource(obj.lightSource?.Id);

        // the way the game moves a knocked chest: same object, new tile; the new place updates the object's own place and tile
        if (location.objects.Remove(this.CarriedTile))
        {
            obj.TileLocation = tile;
            destination.objects[tile] = obj;
        }

        if (hadLight)
        {
            obj.initializeLightSource(tile);
            if (obj.lightSource != null && !destination.hasLightSource(obj.lightSource.Id))
                destination.sharedLights.AddLight(obj.lightSource.Clone());
        }

        this.Drop();
        return true;
    }

    /// <summary>Whether a building has room, worked out once per second rather than every frame.</summary>
    private (GameLocation? Indoors, int Tick, bool Room) RoomCache;

    private bool FindFreeSpotCached(GameLocation indoors)
    {
        if (!ReferenceEquals(this.RoomCache.Indoors, indoors) || Game1.ticks - this.RoomCache.Tick > 60)
            this.RoomCache = (indoors, Game1.ticks, this.FindFreeSpot(indoors) != null);
        return this.RoomCache.Room;
    }

    /// <summary>Get the farm building under the cursor which the carried object can be sent into, with its inside.</summary>
    private (Building Building, GameLocation Indoors)? GetTargetBuilding()
    {
        if (this.Carried is null || Game1.currentLocation is not GameLocation location || !FarmPlaces.IsOnFarm(location))
            return null;

        Building? building = location.getBuildingAt(Game1.currentCursorTile);
        if (building is null || building.isUnderConstruction() || building.GetIndoors() is not GameLocation indoors)
            return null;

        // a place the player can't walk into yet, like the greenhouse before it's repaired
        if (indoors.IsGreenhouse && Game1.getFarm()?.greenhouseUnlocked.Value != true)
            return null;

        return this.CanCarryTo(indoors) ? (building, indoors) : null;
    }

    /// <summary>Find the free spot nearest the door inside a place, keeping the doorway itself clear.</summary>
    private Vector2? FindFreeSpot(GameLocation indoors)
    {
        int width = indoors.Map.Layers[0].LayerWidth;
        int height = indoors.Map.Layers[0].LayerHeight;

        // the doors are where the warps back outside are
        List<Vector2> doors = indoors.warps.Select(warp => new Vector2(warp.X, warp.Y)).ToList();
        Vector2 start = doors.Count > 0 ? doors[0] : new Vector2(width / 2f, height / 2f);

        Vector2? best = null;
        float bestDistance = float.MaxValue;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 tile = new(x, y);

                // leave room to walk in and out
                if (doors.Any(door => Math.Abs(door.X - x) <= 1 && Math.Abs(door.Y - y) <= 2))
                    continue;

                float distance = Vector2.Distance(start, tile);
                if (distance < bestDistance && this.CanPlaceAt(indoors, tile))
                {
                    best = tile;
                    bestDistance = distance;
                }
            }
        }

        return best;
    }

    /// <summary>Get whether the carried object may be put down in a place: the same place, or anywhere on the farm if it comes from the farm.</summary>
    /// <remarks>The farm and its buildings are always kept up to date on every player's game, so the move is safe there in multiplayer too.</remarks>
    private bool CanCarryTo(GameLocation destination)
    {
        if (this.CarriedFrom is not GameLocation source)
            return false;

        return ReferenceEquals(source, destination) || (FarmPlaces.IsOnFarm(source) && FarmPlaces.IsOnFarm(destination));
    }

    /// <summary>Let go of the carried object, which never left its tile.</summary>
    private void Drop()
    {
        this.Carried = null;
        this.CarriedFrom = null;
        WorldTooltip.Paused = false;
    }

    /// <summary>Show where the object would land: a copy under the cursor, on a green or red tile.</summary>
    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (this.Carried is not SObject obj || this.CarriedFrom is not GameLocation source || Game1.currentLocation is not GameLocation location || !this.CanCarryTo(location))
            return;

        try
        {
            // over a farm building: the whole building lights up, the object will go inside
            if (this.GetTargetBuilding() is (Building building, GameLocation indoors))
            {
                bool room = this.FindFreeSpotCached(indoors);
                Vector2 corner = Game1.GlobalToLocal(Game1.viewport, new Vector2(building.tileX.Value, building.tileY.Value) * 64f);
                e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle((int)corner.X, (int)corner.Y, building.tilesWide.Value * 64, building.tilesHigh.Value * 64), (room ? Color.LimeGreen : Color.Red) * 0.35f);
                return;
            }

            bool samePlace = ReferenceEquals(source, location);
            Vector2 tile = Game1.currentCursorTile;
            bool valid = (samePlace && tile == this.CarriedTile) || this.CanPlaceAt(location, tile);

            // the tile it leaves, when it's in sight, and the tile it would land on
            if (samePlace)
            {
                Vector2 from = Game1.GlobalToLocal(Game1.viewport, this.CarriedTile * 64f);
                e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle((int)from.X, (int)from.Y, 64, 64), Color.Yellow * 0.25f);
            }

            Vector2 to = Game1.GlobalToLocal(Game1.viewport, tile * 64f);
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle((int)to.X, (int)to.Y, 64, 64), (valid ? Color.LimeGreen : Color.Red) * 0.35f);

            obj.draw(e.SpriteBatch, (int)tile.X, (int)tile.Y, 0.5f);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the object being moved:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Say how to put the object down or cancel, next to the cursor.</summary>
    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (this.Carried is not SObject obj || this.CarriedFrom is not GameLocation source)
            return;

        // over a building: say it goes inside; away from where it stands: remind the player it's still there until put down
        string hint = this.GetTargetBuilding() is (Building _, GameLocation indoors)
            ? this.Helper.Translation.Get("move-objects.hint-building", new { place = indoors.DisplayName })
            : ReferenceEquals(source, Game1.currentLocation)
                ? this.T("move-objects.hint")
                : this.Helper.Translation.Get("move-objects.hint-elsewhere", new { place = source.DisplayName });
        IClickableMenu.drawHoverText(e.SpriteBatch, hint, Game1.smallFont, boldTitleText: obj.DisplayName);
    }

    /// <summary>Let go when leaving for a place it can't go to: the object stays where it was.</summary>
    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer && this.Carried != null && !this.CanCarryTo(e.NewLocation))
            this.Drop();
    }

    /// <summary>Let go when a menu opens: the object stays where it was.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.NewMenu != null)
            this.Drop();
    }

    /// <summary>Let go at the end of the day: the object stays where it was.</summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.Drop();
    }

    private string T(string key)
    {
        return this.Helper.Translation.Get(key);
    }
}
