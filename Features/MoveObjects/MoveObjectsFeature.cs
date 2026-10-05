using System;
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
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.MoveObjects;

/// <summary>Shift + click a chest, machine or decoration to pick it up, then click where it should go: it moves with everything inside.</summary>
/// <remarks>
/// The game only lets a full chest be knocked one tile at a time, and picking up a working machine throws its work
/// away. Here the very same object changes tile, the way the game itself moves a knocked chest: contents, progress
/// and settings stay, and nothing is ever in the player's hands, so nothing can be lost if the game stops in between.
/// Within the same place only. Things tied to their tile stay put: tappers on their tree, crab pots in their water,
/// garden pots with their plant; furniture is already picked up by a right click.
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
    /// <summary>Pick up with Shift + click; then click to put down, or right-click / Escape to cancel.</summary>
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        try
        {
            if (this.Carried != null)
            {
                if (this.JustPressed(e, SButton.Escape) || this.JustPressed(e, SButton.MouseRight))
                {
                    this.Helper.Input.Suppress(SButton.Escape);
                    this.Helper.Input.Suppress(SButton.MouseRight);
                    this.Drop();
                    Game1.playSound("cancel");
                }
                else if (this.JustPressed(e, SButton.MouseLeft))
                {
                    this.Helper.Input.Suppress(SButton.MouseLeft);
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

    /// <summary>Move the carried object to a tile, in one step.</summary>
    private void PutDown(Vector2 tile)
    {
        SObject? obj = this.Carried;
        GameLocation? location = this.CarriedFrom;
        if (obj is null || location is null)
            return;

        // clicking where it already stands: just let go
        if (tile == this.CarriedTile)
        {
            this.Drop();
            Game1.playSound("cancel");
            return;
        }

        // it may have changed meanwhile (another player, a menu): only move it if it's still there
        if (!ReferenceEquals(location, Game1.currentLocation) || !location.objects.TryGetValue(this.CarriedTile, out SObject? standing) || !ReferenceEquals(standing, obj))
        {
            this.Drop();
            return;
        }

        if (!this.CanPlaceAt(location, tile) || (obj is Chest chest && chest.GetMutex().IsLocked()))
        {
            Game1.playSound("cancel");
            return;
        }

        // the light follows: remove it from the old tile, light it again on the new one
        bool hadLight = obj.lightSource != null;
        location.removeLightSource(obj.lightSource?.Id);

        // the way the game moves a knocked chest: same object, new tile
        if (location.objects.Remove(this.CarriedTile))
        {
            obj.TileLocation = tile;
            location.objects[tile] = obj;
        }

        if (hadLight)
        {
            obj.initializeLightSource(tile);
            if (obj.lightSource != null && !location.hasLightSource(obj.lightSource.Id))
                location.sharedLights.AddLight(obj.lightSource.Clone());
        }

        Game1.playSound("woodyStep");
        this.Drop();
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
        if (this.Carried is not SObject obj || this.CarriedFrom is not GameLocation location || !ReferenceEquals(location, Game1.currentLocation))
            return;

        try
        {
            Vector2 tile = Game1.currentCursorTile;
            bool valid = tile == this.CarriedTile || this.CanPlaceAt(location, tile);

            // the tile it leaves, and the tile it would land on
            Vector2 from = Game1.GlobalToLocal(Game1.viewport, this.CarriedTile * 64f);
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle((int)from.X, (int)from.Y, 64, 64), Color.Yellow * 0.25f);

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
        if (this.Carried is not SObject obj)
            return;

        IClickableMenu.drawHoverText(e.SpriteBatch, this.T("move-objects.hint"), Game1.smallFont, boldTitleText: obj.DisplayName);
    }

    /// <summary>Let go when leaving the place: the object stays where it was.</summary>
    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer)
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
