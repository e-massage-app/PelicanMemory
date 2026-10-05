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
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.MapPins;

/// <summary>A spot the player marked on the map, with their note.</summary>
/// <param name="Id">A short unique ID, to edit or remove the right pin.</param>
/// <param name="Region">The map region it's on (the valley or the island), so it only shows on that map.</param>
/// <param name="X">The horizontal position on the region's map image, in the image's own pixels.</param>
/// <param name="Y">The vertical position on the region's map image, in the image's own pixels.</param>
/// <param name="Note">What the player wrote.</param>
internal record MapPin(string Id, string Region, float X, float Y, string Note);

/// <summary>Right-click on the map to mark a spot with a note; the mark stays on the map, and the note shows on hover.</summary>
/// <remarks>
/// Pure memory: only what the player wrote, where they put it. Pins are stored in the player's own save data, as a
/// position on the map image, so they land exactly where they were clicked whatever the screen size. Clicking a pin
/// edits its note; confirming an empty note removes it.
/// </remarks>
internal class MapPinsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static MapPinsFeature? Instance;

    /// <summary>The key under which pins are saved, in the player's own save data.</summary>
    private const string StoreKey = "map-pins";

    /// <summary>The most characters in a note.</summary>
    private const int MaxNoteLength = 40;

    /// <summary>The most pins a player can keep.</summary>
    private const int MaxPins = 100;

    /// <summary>How close the cursor must be to a pin's centre to point at it, in screen pixels.</summary>
    private const int HitRadius = 20;

    /// <summary>The red cross on the game's cursor sheet.</summary>
    private static readonly Rectangle PinSource = new(269, 471, 14, 15);

    private readonly PlayerStore Store;

    /// <summary>The current player's pins, read once per save.</summary>
    private List<MapPin>? Pins;

    /// <summary>The pin under the cursor, drawn larger.</summary>
    private string? HoveredPinId;

    /// <summary>The game menu to go back to once the note window closes.</summary>
    private GameMenu? MenuToRestore;


    /*********
    ** Public methods
    *********/
    public override string Id => "map-pins";

    public MapPinsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store)
        : base(helper, monitor, harmony, settings)
    {
        this.Store = store;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;

        this.Prefix(AccessTools.Method(typeof(GameMenu), nameof(GameMenu.receiveRightClick)), typeof(MapPinsFeature), nameof(Before_GameMenuRightClick));
        this.Prefix(AccessTools.Method(typeof(MapPage), nameof(MapPage.receiveLeftClick)), typeof(MapPinsFeature), nameof(Before_MapLeftClick));
        this.Prefix(AccessTools.Method(typeof(MapPage), nameof(MapPage.drawMiniPortraits)), typeof(MapPinsFeature), nameof(Before_DrawMiniPortraits));
        this.Postfix(AccessTools.Method(typeof(MapPage), nameof(MapPage.performHoverAction)), typeof(MapPinsFeature), nameof(After_PerformHoverAction));
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.SaveLoaded -= this.OnSaveLoaded;
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        this.Pins = null;
        this.MenuToRestore = null;
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Right-click on the map: mark a new spot, or edit the pin clicked.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_GameMenuRightClick(GameMenu __instance, int x, int y)
    {
        if (Instance is null || __instance.GetCurrentPage() is not MapPage page)
            return true;

        try
        {
            if (Instance.FindPin(page, x, y) is MapPin pin)
            {
                Instance.AskNote(__instance, pin, page.mapRegion.Id, pin.X, pin.Y);
                return false;
            }

            if (!GetScreenMap(page).Contains(x, y))
                return true;

            if (Instance.GetPins().Count >= MaxPins)
            {
                Game1.showRedMessage(Instance.Helper.Translation.Get("map-pins.too-many", new { count = MaxPins }));
                return false;
            }

            Instance.AskNote(__instance, null, page.mapRegion.Id, (x - page.mapBounds.X) / 4f, (y - page.mapBounds.Y) / 4f);
            return false;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to place a pin on the map:\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Left-click on a pin: edit it, rather than let the click close the map.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_MapLeftClick(MapPage __instance, int x, int y)
    {
        if (Instance is null || Game1.activeClickableMenu is not GameMenu menu)
            return true;

        try
        {
            if (Instance.FindPin(__instance, x, y) is not MapPin pin)
                return true;

            Instance.AskNote(menu, pin, __instance.mapRegion.Id, pin.X, pin.Y);
            return false;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to open a pin:\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Draw the pins above the map and its labels, under the players' heads and the tooltip.</summary>
    private static void Before_DrawMiniPortraits(MapPage __instance, SpriteBatch b, float alpha)
    {
        if (Instance is null)
            return;

        try
        {
            foreach (MapPin pin in Instance.GetPins().Where(pin => pin.Region == __instance.mapRegion.Id))
            {
                bool hovered = pin.Id == Instance.HoveredPinId;
                Vector2 position = GetScreenPosition(__instance, pin);
                b.Draw(Game1.mouseCursors, position, PinSource, Color.White * alpha, 0f, new Vector2(PinSource.Width / 2f, PinSource.Height / 2f), hovered ? 2.5f : 2f, SpriteEffects.None, 1f);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the map pins:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Show a pin's note when the cursor is on it.</summary>
    private static void After_PerformHoverAction(MapPage __instance, int x, int y)
    {
        if (Instance is null)
            return;

        try
        {
            MapPin? pin = Instance.FindPin(__instance, x, y);
            Instance.HoveredPinId = pin?.Id;
            if (pin != null)
                __instance.hoverText = pin.Note;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to show a pin's note:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the current player's pins.</summary>
    private List<MapPin> GetPins()
    {
        return this.Pins ??= this.Store.Read<List<MapPin>>(Game1.player, StoreKey) ?? new List<MapPin>();
    }

    /// <summary>Save the current player's pins.</summary>
    private void SavePins()
    {
        this.Store.Write(Game1.player, StoreKey, this.GetPins());
    }

    /// <summary>Get the pin under a screen position on a map page, the nearest if several overlap.</summary>
    private MapPin? FindPin(MapPage page, int x, int y)
    {
        return this.GetPins()
            .Where(pin => pin.Region == page.mapRegion.Id)
            .Select(pin => (Pin: pin, Distance: Vector2.Distance(GetScreenPosition(page, pin), new Vector2(x, y))))
            .Where(match => match.Distance <= HitRadius)
            .OrderBy(match => match.Distance)
            .Select(match => match.Pin)
            .FirstOrDefault();
    }

    /// <summary>Open the note window for a new pin or an existing one.</summary>
    private void AskNote(GameMenu menu, MapPin? existing, string region, float x, float y)
    {
        Game1.playSound("smallSelect");
        this.MenuToRestore = menu;

        Game1.activeClickableMenu = new NoteMenu(
            onDone: note =>
            {
                this.SetNote(existing, region, x, y, note.Trim());
                this.MenuToRestore = null;
                Game1.activeClickableMenu = menu;
            },
            title: this.Helper.Translation.Get(existing is null ? "map-pins.new" : "map-pins.edit"),
            note: existing?.Note ?? "",
            maxLength: MaxNoteLength
        );
    }

    /// <summary>Save what the player wrote: a new pin, a changed note, or a removed pin if the note is empty.</summary>
    private void SetNote(MapPin? existing, string region, float x, float y, string note)
    {
        List<MapPin> pins = this.GetPins();

        if (existing != null)
            pins.RemoveAll(pin => pin.Id == existing.Id);

        if (note.Length > 0)
            pins.Add(new MapPin(existing?.Id ?? Guid.NewGuid().ToString("N")[..8], region, x, y, note));
        else if (existing is null)
            return; // an empty note for a new pin: nothing to do

        Game1.playSound(note.Length > 0 ? "dwop" : "trashcan");
        this.SavePins();
    }

    /// <summary>Forget the previous save's pins, so another save or player starts from its own.</summary>
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.Pins = null;
    }

    /// <summary>Put the map back once the note window closes, whether the player confirmed or cancelled.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (this.MenuToRestore is null || e.OldMenu is not NoteMenu || e.NewMenu != null)
            return;

        Game1.activeClickableMenu = this.MenuToRestore;
        this.MenuToRestore = null;
    }

    /// <summary>Get the map image's rectangle on screen.</summary>
    /// <remarks>The page keeps the map's position in screen pixels but its size in the image's own pixels.</remarks>
    private static Rectangle GetScreenMap(MapPage page)
    {
        return new Rectangle(page.mapBounds.X, page.mapBounds.Y, page.mapBounds.Width * 4, page.mapBounds.Height * 4);
    }

    /// <summary>Get where a pin's centre is on screen.</summary>
    private static Vector2 GetScreenPosition(MapPage page, MapPin pin)
    {
        return new Vector2(page.mapBounds.X + pin.X * 4, page.mapBounds.Y + pin.Y * 4);
    }
}
