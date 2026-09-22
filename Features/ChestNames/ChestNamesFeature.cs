using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Objects;

namespace PelicanMemory.Features.ChestNames;

/// <summary>Lets the player name a chest, so their own windows can say "workshop" instead of "red chest, the farm".</summary>
/// <remarks>
/// The button sits under the chest menu's own column of buttons, and uses the game's own naming window rather than a
/// home-made text field. The name is shown when hovering the button, since the chest menu has no free strip to write
/// in, and everywhere the mod names that chest. It lives in the chest's mod data, so it is saved with the game and
/// shared with the other player in multiplayer.
/// </remarks>
internal class ChestNamesFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static ChestNamesFeature? Instance;

    /// <summary>The gap between the button and the one above it.</summary>
    private const int ButtonSpacing = 4;

    /// <summary>The chest menu to reopen once the naming window closes.</summary>
    private ItemGrabMenu? MenuToRestore;


    /*********
    ** Public methods
    *********/
    public override string Id => "chest-names";

    public ChestNamesFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;

        this.Postfix(
            AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.draw), new[] { typeof(SpriteBatch) }),
            typeof(ChestNamesFeature),
            nameof(After_Draw)
        );

        this.Prefix(
            AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveLeftClick)),
            typeof(ChestNamesFeature),
            nameof(Before_ReceiveLeftClick)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        this.MenuToRestore = null;
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the name plate and the button which opens the naming window.</summary>
    private static void After_Draw(ItemGrabMenu __instance, SpriteBatch b)
    {
        if (Instance is null || GetChest(__instance) is not Chest chest)
            return;

        try
        {
            ClickableTextureComponent button = GetButton(__instance);
            button.draw(b);

            // the chest's own menu has no free strip to write in, so the name is shown where it can't overlap anything
            if (button.containsPoint(Game1.getOldMouseX(), Game1.getOldMouseY()))
            {
                string? name = ChestLabels.Get(chest);
                string hover = name != null
                    ? name + Environment.NewLine + Instance.Helper.Translation.Get("chest-names.rename")
                    : Instance.Helper.Translation.Get("chest-names.button");

                IClickableMenu.drawHoverText(b, hover, Game1.smallFont);
                __instance.drawMouse(b);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the chest name:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Open the naming window when the button is clicked.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_ReceiveLeftClick(ItemGrabMenu __instance, int x, int y)
    {
        if (Instance is null || GetChest(__instance) is not Chest chest)
            return true;

        try
        {
            if (!GetButton(__instance).containsPoint(x, y))
                return true;

            Instance.AskName(__instance, chest);
            return false;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to open the naming window:\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Open the game's naming window for a chest.</summary>
    private void AskName(ItemGrabMenu menu, Chest chest)
    {
        Game1.playSound("smallSelect");
        this.MenuToRestore = menu;

        Game1.activeClickableMenu = new NamingMenu(
            b: name =>
            {
                ChestLabels.Set(chest, name);
                this.MenuToRestore = null;
                Game1.activeClickableMenu = menu;
            },
            title: this.Helper.Translation.Get("chest-names.prompt"),
            defaultName: ChestLabels.Get(chest) ?? string.Empty
        );
    }

    /// <summary>Put the chest back once the naming window closes, whether the player confirmed or cancelled.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (this.MenuToRestore is null || e.OldMenu is not NamingMenu || e.NewMenu != null)
            return;

        Game1.activeClickableMenu = this.MenuToRestore;
        this.MenuToRestore = null;
    }

    /// <summary>Get the chest a menu was opened from, or <c>null</c> if it isn't one of the player's chests.</summary>
    private static Chest? GetChest(ItemGrabMenu menu)
    {
        Chest? chest = menu.sourceItem as Chest ?? menu.context as Chest;
        return chest?.playerChest.Value == true ? chest : null;
    }

    /// <summary>Get the naming button, at the bottom of the menu's own column of buttons.</summary>
    /// <remarks>
    /// Anchored to the buttons the game actually created, never recomputed: a large chest widens its grid past the
    /// menu frame and shifts the whole menu down afterwards, so any position worked out by hand lands on the slots.
    /// Below the confirm button is the one spot the grid can never reach, whatever the chest's size.
    /// </remarks>
    private static ClickableTextureComponent GetButton(ItemGrabMenu menu)
    {
        Rectangle column = (menu.okButton ?? menu.trashCan)?.bounds
            ?? new Rectangle(menu.xPositionOnScreen + menu.width + 4, menu.yPositionOnScreen + menu.height - 192, 64, 64);

        // the whole sign, post included, scaled to sit in the column like the game's own icons
        ParsedItemData sign = ItemRegistry.GetDataOrErrorItem("(BC)37");
        Rectangle source = sign.GetSourceRect();
        float scale = 64f / Math.Max(source.Width, source.Height);

        Rectangle area = new(column.X + (64 - (int)(source.Width * scale)) / 2, column.Bottom + ButtonSpacing, (int)(source.Width * scale), (int)(source.Height * scale));
        return new ClickableTextureComponent(area, sign.GetTexture(), source, scale);
    }
}
