using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.GameData.Buildings;
using StardewValley.TokenizableStrings;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.HorseActions;

/// <summary>Lets the player use chests, machines and doors from their horse, and stops the horse blocking the way.</summary>
/// <remarks>
/// <para>
/// The game refuses every placed object while riding (except gates), and any other click from the saddle gets you off
/// the horse before it even looks for a door under the cursor. So:
/// </para>
/// <list type="bullet">
///   <item>placed objects (chests, machines, signs…) are used without getting off, exactly as the game does on foot;</item>
///   <item>scenery actions (shops, boards, town doors…) are done from the saddle — a door leading indoors gets you
///   off by itself, leaving the horse outside;</item>
///   <item>once you're off, you walk through your horse: the game makes it solid again, which blocks doors;</item>
///   <item>the reach is measured from the horse as drawn rather than from the saddle, since the horse is bigger than
///   the player and the game would otherwise ignore the cursor on a chest right in front of its head.</item>
/// </list>
/// <para>Farm buildings (the house included) refuse every click while riding: their doors get you off and in, and
/// everything else they do (mailbox, silo, animal door) is done from the saddle.</para>
/// </remarks>
internal class HorseActionsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static HorseActionsFeature? Instance;

    /// <summary>Whether the game is handling a click made from the saddle, during which any click counts as a right-click.</summary>
    /// <remarks>
    /// Chests, the shipping bin and the animal door only answer a right-click. On horseback the game sends the left
    /// click to the same place (tools can't be used from the saddle), where it would be refused and get you off the
    /// horse instead. Never set when a villager or another player is on the tile, so a left click can't give them the
    /// item in hand; always reset by a finalizer, so it can't leak outside that click.
    /// </remarks>
    private static bool ClickFromSaddle;


    /*********
    ** Public methods
    *********/
    public override string Id => "horse-actions";

    public HorseActionsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        var checkAction = AccessTools.Method(typeof(GameLocation), nameof(GameLocation.checkAction), new[] { typeof(xTile.Dimensions.Location), typeof(xTile.Dimensions.Rectangle), typeof(Farmer) });
        this.Prefix(checkAction, typeof(HorseActionsFeature), nameof(Before_CheckAction));
        this.Postfix(checkAction, typeof(HorseActionsFeature), nameof(After_CheckAction));
        this.Finalizer(checkAction, typeof(HorseActionsFeature), nameof(Finally_CheckAction));

        this.Prefix(
            AccessTools.Method(typeof(Game1), nameof(Game1.didPlayerJustRightClick)),
            typeof(HorseActionsFeature),
            nameof(Before_DidPlayerJustRightClick)
        );

        this.Postfix(
            AccessTools.Method(typeof(Horse), nameof(Horse.dismount)),
            typeof(HorseActionsFeature),
            nameof(After_Dismount)
        );

        this.Prefix(
            AccessTools.Method(typeof(Utility), nameof(Utility.tileWithinRadiusOfPlayer)),
            typeof(HorseActionsFeature),
            nameof(Before_TileWithinRadiusOfPlayer)
        );
    }

    protected override void OnDisable()
    {
        ClickFromSaddle = false;
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Handle the clicks the game would answer by getting you off the horse.</summary>
    /// <remarks>
    /// On horseback the game gets you off instead of looking at what's under the cursor. Here, only the door of a farm
    /// building gets you off (the building refuses riders before opening), and the game then takes you in. Everything
    /// else is done from the saddle: building actions like the mailbox or the silo, the animal door, and scenery
    /// actions like shop counters or town doors — a door leading indoors gets you off by itself as you go through,
    /// leaving the horse outside.
    /// </remarks>
    /// <returns>Returns whether to run the game's own handling.</returns>
    private static bool Before_CheckAction(GameLocation __instance, xTile.Dimensions.Location tileLocation, Farmer who, ref bool __result)
    {
        if (!IsLocalRider(__instance, who))
            return true;

        try
        {
            Vector2 tile = new(tileLocation.X, tileLocation.Y);

            // a villager or another player there keeps the game's usual behaviour, so a left click can't give them a gift
            bool someoneThere = __instance.isCharacterAtTile(tile) != null || IsFarmerAtTile(__instance, tile);
            if (!someoneThere)
                ClickFromSaddle = true;

            // placed objects are handled from the saddle, after the game's own checks (see below)
            if (__instance.objects.ContainsKey(tile))
                return true;

            if (IsBuildingDoor(__instance, tile))
            {
                who.mount.dismount();
                return true;
            }

            if (someoneThere)
                return true;

            if (TryUseBuilding(__instance, tile, who))
            {
                __result = true;
                return false;
            }

            string? action = __instance.doesTileHaveProperty(tileLocation.X, tileLocation.Y, "Action", "Buildings");
            if (action != null)
            {
                // if the action does nothing, the game's usual answer follows: getting off the horse
                __result = __instance.performAction(action, who, tileLocation);
                return false;
            }

            // the top half of a tall object: a chest or machine is drawn two tiles high but only stands on its bottom
            // tile. On foot this half is out of reach, so the game falls back to the tile in front, which happens to
            // be the object; from the wider reach of the saddle it's in reach and empty, so it's redirected here.
            Vector2 below = tile + new Vector2(0, 1);
            if (__instance.objects.TryGetValue(below, out SObject? tall) && tall.bigCraftable.Value && IsUsable(tall) && !IsBuildingTile(__instance, tile))
            {
                __result = UseObject(__instance, below, tall, who);
                return false;
            }
        }
        catch (Exception ex)
        {
            Instance!.Monitor.LogOnce($"Failed to handle a click from the horse:{Environment.NewLine}{ex}", LogLevel.Error);
        }

        return true;
    }

    /// <summary>Use a placed object from the saddle, when the game refused it only because the player is riding.</summary>
    /// <remarks>Done after the game's own checks, so buildings, villagers and gates keep their usual priority.</remarks>
    private static void After_CheckAction(GameLocation __instance, xTile.Dimensions.Location tileLocation, Farmer who, ref bool __result)
    {
        if (__result || !IsLocalRider(__instance, who))
            return;

        try
        {
            Vector2 tile = new(tileLocation.X, tileLocation.Y);
            if (__instance.objects.TryGetValue(tile, out SObject? obj) && IsUsable(obj))
                __result = UseObject(__instance, tile, obj, who);
        }
        catch (Exception ex)
        {
            Instance!.Monitor.LogOnce($"Failed to use an object from the horse:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Measure the reach from the whole horse instead of the saddle.</summary>
    /// <remarks>
    /// The game only uses the cursor if it's within one tile of the player, and falls back to the tile in front
    /// otherwise. On horseback the player's tile is the middle of the horse's hooves, and the horse is drawn two tiles
    /// tall above them, so a chest right in front of its head was out of reach when facing up. The reach here covers
    /// one tile around the horse as drawn, which always includes the game's own. A tall object (a chest, a machine)
    /// counts as in reach when its base is: it's drawn over the tile above too, and a click there must not fall back
    /// to the empty tile in front of the horse.
    /// </remarks>
    private static bool Before_TileWithinRadiusOfPlayer(int xTile, int yTile, int tileRadius, Farmer f, ref bool __result)
    {
        if (Instance is null || tileRadius != 1 || f != Game1.player || !f.isRidingHorse())
            return true;

        // the collision box is only the hooves (half a tile high): the horse as drawn is its sprite standing on it
        Rectangle hooves = f.mount.GetBoundingBox();
        int width = f.mount.Sprite.SpriteWidth * Game1.pixelZoom;
        int height = f.mount.Sprite.SpriteHeight * Game1.pixelZoom;
        Rectangle horse = new(hooves.Center.X - width / 2, hooves.Bottom - height, width, height);

        int left = horse.Left / Game1.tileSize;
        int right = (horse.Right - 1) / Game1.tileSize;
        int top = horse.Top / Game1.tileSize;
        int bottom = (horse.Bottom - 1) / Game1.tileSize;

        bool InReach(int x, int y) => x >= left - 1 && x <= right + 1 && y >= top - 1 && y <= bottom + 1;

        // a tall object is in reach when its base is, whichever half of it is clicked
        __result = InReach(xTile, yTile)
            || (InReach(xTile, yTile + 1) && f.currentLocation?.objects.TryGetValue(new Vector2(xTile, yTile + 1), out SObject? tall) == true && tall.bigCraftable.Value);
        return false;
    }

    /// <summary>Get whether a tile is the door of a farm building, which refuses riders.</summary>
    private static bool IsBuildingDoor(GameLocation location, Vector2 tile)
    {
        foreach (Building building in location.buildings)
        {
            if (building.daysOfConstructionLeft.Value <= 0
                && building.GetIndoors() != null
                && tile.X == building.tileX.Value + building.humanDoor.X
                && tile.Y == building.tileY.Value + building.humanDoor.Y)
                return true;
        }

        return false;
    }

    /// <summary>Do what a farm building does when clicked — its animal door or an action like the mailbox — from the saddle.</summary>
    /// <remarks>Same checks as the game's own building click, minus the refusal of riders it starts with.</remarks>
    private static bool TryUseBuilding(GameLocation location, Vector2 tile, Farmer who)
    {
        foreach (Building building in location.buildings)
        {
            if (building.daysOfConstructionLeft.Value > 0 || building.GetData() is not BuildingData data)
                continue;

            Rectangle door = building.getRectForAnimalDoor(data);
            if (door != Rectangle.Empty
                && new Rectangle(door.X / Game1.tileSize, door.Y / Game1.tileSize, door.Width / Game1.tileSize, door.Height / Game1.tileSize).Contains((int)tile.X, (int)tile.Y)
                && Game1.didPlayerJustRightClick(ignoreNonMouseHeldInput: true))
            {
                building.ToggleAnimalDoor(who);
                return true;
            }

            if (building.occupiesTile(tile, applyTilePropertyRadius: true) && !building.isTilePassable(tile))
            {
                string? action = data.GetActionAtTile((int)tile.X - building.tileX.Value, (int)tile.Y - building.tileY.Value);
                if (action != null && location.performAction(TokenParser.ParseText(action), who, new xTile.Dimensions.Location((int)tile.X, (int)tile.Y)))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Get whether a tile is part of a farm building, whose own click must not be taken over.</summary>
    private static bool IsBuildingTile(GameLocation location, Vector2 tile)
    {
        foreach (Building building in location.buildings)
        {
            if (building.occupiesTile(tile))
                return true;
        }

        return false;
    }

    /// <summary>Get whether another player stands on a tile.</summary>
    private static bool IsFarmerAtTile(GameLocation location, Vector2 tile)
    {
        foreach (Farmer farmer in location.farmers)
        {
            if (farmer != Game1.player && farmer.Tile == tile)
                return true;
        }

        return false;
    }

    /// <summary>End the click made from the saddle, whatever happened during it.</summary>
    private static Exception? Finally_CheckAction(Exception? __exception)
    {
        ClickFromSaddle = false;
        return __exception;
    }

    /// <summary>Count any click made from the saddle as a right-click.</summary>
    private static bool Before_DidPlayerJustRightClick(ref bool __result)
    {
        if (!ClickFromSaddle)
            return true;

        __result = true;
        return false;
    }

    /// <summary>Let the player walk through their horse once they're off it.</summary>
    private static void After_Dismount(Horse __instance)
    {
        if (Instance is not null)
            __instance.farmerPassesThrough = true;
    }

    /// <summary>Get whether this is the local player, riding, in a normal moment of play.</summary>
    private static bool IsLocalRider(GameLocation location, Farmer who)
    {
        return Instance is not null
            && who == Game1.player
            && who.isRidingHorse()
            && location.currentEvent is null;
    }

    /// <summary>Get whether an object is one the game lets you use on foot but refuses on horseback: a chest, a machine, a sign…</summary>
    /// <remarks>Gates are left alone: the game already lets riders use them.</remarks>
    private static bool IsUsable(SObject obj)
    {
        return obj is not Fence && obj.Type is "Crafting" or "interactive";
    }

    /// <summary>Use an object exactly as the game does on foot: open or collect it, or put the held item in.</summary>
    private static bool UseObject(GameLocation location, Vector2 tile, SObject obj, Farmer who)
    {
        if (who.ActiveObject == null && obj.checkForAction(who))
            return true;

        // the object may have been removed by the action above
        if (!location.objects.TryGetValue(tile, out obj))
            return false;

        if (who.CurrentItem == null)
            return obj.checkForAction(who);

        // same order as the game: check whether the item fits, then put it in
        SObject? held = obj.heldObject.Value;
        obj.heldObject.Value = null;
        bool accepts = obj.performObjectDropInAction(who.CurrentItem, probe: true, who);
        obj.heldObject.Value = held;

        bool dropped = obj.performObjectDropInAction(who.CurrentItem, probe: false, who, returnFalseIfItemConsumed: true);
        if ((accepts || dropped) && who.isMoving())
            Game1.haltAfterCheck = false;

        if (who.ignoreItemConsumptionThisFrame)
            return true;

        if (dropped)
        {
            who.reduceActiveItemByOne();
            return true;
        }

        return obj.checkForAction(who) || accepts;
    }
}
