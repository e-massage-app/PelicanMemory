using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.Loadouts;

/// <summary>Outfit presets, like in MMORPGs: save what you wear and carry, put it all back on in one click.</summary>
/// <remarks>
/// Made for the days spent waiting for a lucky day to go down the Skull Cavern: preparing the bag every evening and
/// undoing it every morning. A button in the inventory opens five presets. Saving takes a snapshot of the equipment and
/// of every bag slot (quantities included). Applying takes everything off, then takes each item from what the player
/// had and the farm's chests — the very same enchanted weapon or combined ring first — and puts the rest back where it
/// came from. What can't be found is listed in the chat; nothing is ever replaced by something else.
///
/// Only on the farm, with the farm's chests (the player's rule for every convenience: nothing from a cave).
/// </remarks>
internal class LoadoutsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    public const int MaxLoadouts = 5;

    private const string LoadoutsKey = "loadouts";
    private const string OriginsKey = "loadout-origins";

    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static LoadoutsFeature? Instance;

    /// <summary>The frame of the game's "add to existing stacks" button, emptied to hold the shirt (like "Put away everywhere").</summary>
    private static readonly Rectangle FrameSource = new(103, 469, 16, 16);

    /// <summary>The farmer shirt drawn in the button (a teal one, which stands out on the orange frame).</summary>
    private const int ShirtIndex = 9;

    private static Texture2D? ButtonTexture;

    private readonly PlayerStore Store;

    private List<Loadout?>? Loadouts;


    /*********
    ** Public methods
    *********/
    public override string Id => "loadouts";

    public LoadoutsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store)
        : base(helper, monitor, harmony, settings)
    {
        this.Store = store;
    }

    /// <summary>The five slots, empty ones as <c>null</c>.</summary>
    public IReadOnlyList<Loadout?> GetLoadouts()
    {
        if (this.Loadouts is null)
        {
            this.Loadouts = this.Store.Read<List<Loadout?>>(Game1.player, LoadoutsKey) ?? new List<Loadout?>();
            while (this.Loadouts.Count < MaxLoadouts)
                this.Loadouts.Add(null);
        }
        return this.Loadouts;
    }

    /// <summary>Whether a preset can be applied here: on the farm, outside events and festivals.</summary>
    public bool CanApplyHere()
    {
        return Context.IsWorldReady && FarmPlaces.IsOnFarm(Game1.currentLocation) && !Game1.eventUp && !Game1.isFestival();
    }

    /// <summary>Save the current outfit in a slot: straight away in an empty one, after asking over an existing one.</summary>
    public void AskSave(int index)
    {
        Loadout? current = this.GetLoadouts()[index];
        if (current is null)
        {
            this.AskName(this.T("loadouts.default-name", new { number = index + 1 }), name => this.Save(index, name));
            return;
        }

        Game1.activeClickableMenu = new ConfirmationDialog(
            this.T("loadouts.confirm-overwrite", new { name = current.Name }),
            _ => { this.Save(index, current.Name); this.OpenMenu(); },
            _ => this.OpenMenu());
    }

    /// <summary>Rename a preset.</summary>
    public void AskRename(int index)
    {
        if (this.GetLoadouts()[index] is not Loadout loadout)
            return;
        this.AskName(loadout.Name, name =>
        {
            this.Loadouts![index] = loadout with { Name = name };
            this.Write();
        });
    }

    /// <summary>Delete a preset, after asking.</summary>
    public void AskDelete(int index)
    {
        if (this.GetLoadouts()[index] is not Loadout loadout)
            return;
        Game1.activeClickableMenu = new ConfirmationDialog(
            this.T("loadouts.confirm-delete", new { name = loadout.Name }),
            _ => { this.Loadouts![index] = null; this.Write(); this.OpenMenu(); },
            _ => this.OpenMenu());
    }

    /// <summary>Put a preset on, and say what happened.</summary>
    public void Apply(int index)
    {
        if (this.GetLoadouts()[index] is not Loadout loadout || !this.CanApplyHere())
            return;

        try
        {
            Dictionary<string, ChestRef> origins = this.Store.Read<Dictionary<string, ChestRef>>(Game1.player, OriginsKey) ?? new();
            LoadoutReport report = LoadoutApplier.Apply(Game1.player, loadout, FarmChests.Get(), origins);
            this.Store.Write(Game1.player, OriginsKey, origins);
            this.Report(loadout, report);
            Game1.playSound("dwop");

            // items with no chest of their own: the player chooses where they go (remembered for next time)
            if (report.Homeless.Count > 0)
            {
                Game1.activeClickableMenu = new ChooseChestMenu(this.Helper.Translation, report.Homeless, FarmChests.Get(), (item, chest) =>
                {
                    Dictionary<string, ChestRef> known = this.Store.Read<Dictionary<string, ChestRef>>(Game1.player, OriginsKey) ?? new();
                    known[Loadout.KeyOf(item)] = ChestRef.Of(chest);
                    this.Store.Write(Game1.player, OriginsKey, known);
                });
            }
            else
                Game1.exitActiveMenu();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to apply the preset « {loadout.Name} »:\n{ex}", LogLevel.Error);
            Game1.addHUDMessage(new HUDMessage(this.T("loadouts.failed"), HUDMessage.error_type));
        }
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        this.Postfix(AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.draw), new[] { typeof(SpriteBatch) }), typeof(LoadoutsFeature), nameof(After_Draw));
        this.Prefix(AccessTools.Method(typeof(InventoryPage), nameof(InventoryPage.receiveLeftClick)), typeof(LoadoutsFeature), nameof(Before_ReceiveLeftClick));
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.SaveLoaded -= this.OnSaveLoaded;
        this.Loadouts = null;
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Draw the presets button under the game's "organize" button, with its tooltip.</summary>
    private static void After_Draw(InventoryPage __instance, SpriteBatch b)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            Rectangle area = GetButtonArea(__instance);
            bool hovered = area.Contains(Game1.getMouseX(), Game1.getMouseY());
            // grows a little on hover, like the game's buttons beside it
            float scale = hovered ? 4.2f : 4f;
            Vector2 centre = new(area.Center.X, area.Center.Y);
            b.Draw(GetButtonTexture(), centre, new Rectangle(0, 0, 16, 16), Color.White, 0f, new Vector2(8, 8), scale, SpriteEffects.None, 0.88f);
            if (hovered && Game1.player.CursorSlotItem is null)
                IClickableMenu.drawHoverText(b, Instance.T("loadouts.button"), Game1.smallFont);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the presets button:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Open the presets when the button is clicked (with nothing held, so no item gets lost).</summary>
    private static bool Before_ReceiveLeftClick(InventoryPage __instance, int x, int y)
    {
        if (Instance is null || !Context.IsWorldReady || !GetButtonArea(__instance).Contains(x, y))
            return true;
        if (Game1.player.CursorSlotItem != null)
        {
            Game1.playSound("cancel");
            return false;
        }

        Game1.playSound("bigSelect");
        Instance.OpenMenu();
        return false;
    }


    /*********
    ** Private methods
    *********/
    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }

    private void OnSaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
    {
        this.Loadouts = null;
    }

    /// <summary>The button's place: in the column right of the inventory, under the game's "organize" button.</summary>
    private static Rectangle GetButtonArea(InventoryPage page)
    {
        Rectangle organize = page.organizeButton?.bounds ?? new Rectangle(page.xPositionOnScreen + page.width, page.yPositionOnScreen + page.height / 3 - 56, 64, 64);
        return new Rectangle(organize.X, organize.Bottom + 8, 64, 64);
    }

    /// <summary>Build the button's picture from the game's sprites: the framed button, with a farmer's shirt inside
    /// (its torso, and sleeves in the torso's own side colours, as the game draws shirts).</summary>
    private static Texture2D GetButtonTexture()
    {
        if (ButtonTexture is { IsDisposed: false })
            return ButtonTexture;

        Color[] pixels = new Color[16 * 16];
        Game1.mouseCursors.GetData(0, FrameSource, pixels, 0, pixels.Length);
        Color background = pixels[2 * 16 + 2];
        for (int y = 2; y <= 14; y++)
            for (int x = 2; x <= 13; x++)
                pixels[y * 16 + x] = background;

        Texture2D shirts = FarmerRenderer.shirtsTexture;
        Color[] torso = new Color[8 * 8];
        shirts.GetData(0, new Rectangle(ShirtIndex % (shirts.Width / 8) * 8, ShirtIndex / (shirts.Width / 8) * 32, 8, 8), torso, 0, torso.Length);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                if (torso[y * 8 + x].A > 0)
                    pixels[(4 + y) * 16 + 4 + x] = torso[y * 8 + x];
        for (int dy = 0; dy < 4; dy++)
        {
            Color left = torso[Math.Min(3, 1 + dy) * 8], right = torso[Math.Min(3, 1 + dy) * 8 + 7];
            pixels[(5 + dy) * 16 + 2] = pixels[(5 + dy) * 16 + 3] = left;
            pixels[(5 + dy) * 16 + 12] = pixels[(5 + dy) * 16 + 13] = right;
        }

        ButtonTexture = new Texture2D(Game1.graphics.GraphicsDevice, 16, 16);
        ButtonTexture.SetData(pixels);
        return ButtonTexture;
    }

    private void OpenMenu()
    {
        Game1.activeClickableMenu = new LoadoutsMenu(this, this.Helper.Translation);
    }

    /// <summary>Ask a name with the game's naming box, then come back to the presets.</summary>
    private void AskName(string defaultName, Action<string> done)
    {
        Game1.activeClickableMenu = new NamingMenu(name =>
        {
            name = name?.Trim() ?? "";
            if (name.Length > 0)
            {
                done(name);
                Game1.playSound("coin");
            }
            this.OpenMenu();
        }, this.T("loadouts.name-title"), defaultName);
    }

    private void Save(int index, string name)
    {
        this.GetLoadouts();
        this.Loadouts![index] = Loadout.Capture(Game1.player, name);
        this.Write();
    }

    private void Write()
    {
        this.Store.Write(Game1.player, LoadoutsKey, this.Loadouts);
    }

    /// <summary>Say what applying did: in the chat what's missing (as asked), and a short summary on screen.</summary>
    private void Report(Loadout loadout, LoadoutReport report)
    {
        foreach ((string name, int missing) in report.Missing)
            Game1.chatBox?.addInfoMessage(this.T("loadouts.missing-line", new { name, count = missing }));
        foreach (string name in report.Dropped)
            Game1.chatBox?.addInfoMessage(this.T("loadouts.dropped-line", new { name }));

        string summary = this.T("loadouts.applied", new { name = loadout.Name, count = report.PutAway });
        if (report.Missing.Count > 0)
            summary += " " + this.T("loadouts.applied-missing", new { count = report.Missing.Count });
        Game1.addHUDMessage(new HUDMessage(summary, report.Missing.Count > 0 ? HUDMessage.error_type : HUDMessage.newQuest_type));
    }
}
