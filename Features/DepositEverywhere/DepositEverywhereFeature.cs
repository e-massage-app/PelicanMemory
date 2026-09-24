using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;

namespace PelicanMemory.Features.DepositEverywhere;

/// <summary>A button in the farm's chests which puts each item of the bag in the chest that already holds it.</summary>
/// <remarks>
/// <para>
/// The game's "add to existing stacks" button does this for the open chest only. This one does it for every chest on
/// the farm at once: each item goes to the chest which already holds the most of it, topping up the stacks there and
/// then using a free slot of that same chest. An item with no home anywhere stays in the bag.
/// </para>
/// <para>
/// Deliberately limited to the farm, and to a button inside a chest: sending the bag home from a cave would be
/// cheating. The toolbar row is left alone, so what the player holds in hand stays there.
/// </para>
/// </remarks>
internal class DepositEverywhereFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static DepositEverywhereFeature? Instance;

    /// <summary>How many slots the toolbar row holds, which are never emptied.</summary>
    private const int ToolbarSize = 12;

    /// <summary>How many item names the summary lists before shortening.</summary>
    private const int SummaryLength = 4;

    /// <summary>The game's own "add to existing stacks" button, whose frame this button reuses.</summary>
    private static readonly Rectangle FrameSource = new(103, 469, 16, 16);

    /// <summary>The small chest the game draws beside a chest's slots, which fits inside a button.</summary>
    private static readonly Rectangle ChestIconSource = new(127, 412, 10, 11);

    /// <summary>The button's picture, built once from the game's own sprites.</summary>
    private static Texture2D? ButtonTexture;

    /// <summary>The button, kept between frames so it grows on hover like the game's buttons.</summary>
    private static ClickableTextureComponent? Button;


    /*********
    ** Public methods
    *********/
    public override string Id => "deposit-everywhere";

    public DepositEverywhereFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Postfix(
            AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.draw), new[] { typeof(SpriteBatch) }),
            typeof(DepositEverywhereFeature),
            nameof(After_Draw)
        );

        this.Prefix(
            AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveLeftClick)),
            typeof(DepositEverywhereFeature),
            nameof(Before_ReceiveLeftClick)
        );
    }

    protected override void OnDisable()
    {
        ButtonTexture?.Dispose();
        ButtonTexture = null;
        Button = null;
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the button, and its tooltip while hovered.</summary>
    private static void After_Draw(ItemGrabMenu __instance, SpriteBatch b)
    {
        if (Instance is null || !IsFarmStorageMenu(__instance) || FindButton(__instance) is not Rectangle button)
            return;

        try
        {
            DrawButton(b, button);

            if (button.Contains(Game1.getOldMouseX(), Game1.getOldMouseY()) && __instance.heldItem == null)
            {
                IClickableMenu.drawHoverText(b, Instance.Helper.Translation.Get("deposit.hover"), Game1.smallFont, boldTitleText: Instance.Helper.Translation.Get("deposit.title"));
                __instance.drawMouse(b);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the deposit button:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Put everything away when the button is clicked.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_ReceiveLeftClick(ItemGrabMenu __instance, int x, int y)
    {
        if (Instance is null || __instance.heldItem != null || !IsFarmStorageMenu(__instance) || FindButton(__instance) is not Rectangle button || !button.Contains(x, y))
            return true;

        try
        {
            Instance.Deposit();
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to put the bag away:\n{ex}", LogLevel.Error);
        }

        return false;
    }

    /// <summary>Send each item of the bag (toolbar aside) to the farm chest which already holds the most of it.</summary>
    private void Deposit()
    {
        HashSet<Chest> used = new();
        Dictionary<string, int> moved = Store(Game1.player.Items, ToolbarSize, GetFarmChests(), used);
        this.ShowSummary(moved, used.Count);
    }

    /// <summary>Move each item of a bag to the chest which already holds the most of it, whatever its quality.</summary>
    /// <param name="bag">The items to put away; emptied slots are set to <c>null</c>.</param>
    /// <param name="firstSlot">The first slot to put away, so the toolbar row before it is left alone.</param>
    /// <param name="chests">The chests items may go to.</param>
    /// <param name="used">Filled with the chests which received something.</param>
    /// <returns>How many of each item (by display name) were put away.</returns>
    /// <remarks>Kept apart from the game's state, so it can be checked outside the game.</remarks>
    internal static Dictionary<string, int> Store(IList<Item> bag, int firstSlot, IReadOnlyList<Chest> chests, ISet<Chest> used)
    {
        Dictionary<string, int> moved = new();

        for (int i = firstSlot; i < bag.Count; i++)
        {
            if (bag[i] is not Item item || item is Tool)
                continue;

            string id = item.QualifiedItemId;
            string name = item.DisplayName;

            // the chests which already hold it, whatever the quality, the fullest first
            List<Chest> homes = chests
                .Select(chest => (Chest: chest, Count: chest.GetItemsForPlayer().Where(stored => stored?.QualifiedItemId == id).Sum(stored => stored.Stack)))
                .Where(home => home.Count > 0)
                .OrderByDescending(home => home.Count)
                .Select(home => home.Chest)
                .ToList();

            foreach (Chest home in homes)
            {
                // the game shrinks the stack it's given to what didn't fit and hands that same stack back, and when
                // the whole stack fits in a free slot it puts that very object in the chest: so count from the
                // quantity before, and empty the bag slot ourselves once nothing is left
                int before = item.Stack;
                Item? rest = home.addItem(item);
                int done = before - (rest?.Stack ?? 0);

                if (done > 0)
                {
                    moved[name] = moved.GetValueOrDefault(name) + done;
                    used.Add(home);
                }

                if (rest is null)
                {
                    bag[i] = null!;
                    break;
                }

                item = rest;
            }
        }

        return moved;
    }

    /// <summary>Tell the player what was put away, or that nothing had a home.</summary>
    private void ShowSummary(Dictionary<string, int> moved, int chestCount)
    {
        if (moved.Count == 0)
        {
            Game1.playSound("cancel");
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("deposit.nothing"), HUDMessage.error_type));
            return;
        }

        Game1.playSound("Ship");

        List<string> parts = moved
            .OrderByDescending(pair => pair.Value)
            .Take(SummaryLength)
            .Select(pair => $"{pair.Value} {pair.Key}")
            .ToList();
        if (moved.Count > SummaryLength)
            parts.Add(this.Helper.Translation.Get("deposit.more", new { count = moved.Count - SummaryLength }));

        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("deposit.done", new { items = string.Join(", ", parts), chests = chestCount })));
    }

    /// <summary>Get the storage chests of the farm and its buildings which nobody else has open.</summary>
    private static List<Chest> GetFarmChests()
    {
        List<Chest> chests = new();
        HashSet<object> inventories = new(ReferenceEqualityComparer.Instance);

        StorageIndex.ForEachContainer((chest, location) =>
        {
            if (!IsOnFarm(location) || !IsStorage(chest))
                return;

            // opened by the other player at this very moment: leave it to them
            if (chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld())
                return;

            // junimo chests all share one inventory: count it once
            if (inventories.Add(chest.GetItemsForPlayer()))
                chests.Add(chest);
        });

        return chests;
    }

    /// <summary>Get whether a menu is a storage chest or fridge on the farm, where the button belongs.</summary>
    private static bool IsFarmStorageMenu(ItemGrabMenu menu)
    {
        return menu.source == ItemGrabMenu.source_chest
            && (menu.sourceItem as Chest ?? menu.context as Chest) is Chest chest
            && IsStorage(chest)
            && Game1.currentLocation is not null
            && IsOnFarm(Game1.currentLocation);
    }

    /// <summary>Get whether a chest is meant for storage, rather than a shipping bin, hopper or other machine-like chest.</summary>
    private static bool IsStorage(Chest chest)
    {
        return chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest or Chest.SpecialChestTypes.JunimoChest;
    }

    /// <summary>Get whether a location is the farm or one of its buildings (the house, sheds, barns, the greenhouse…).</summary>
    private static bool IsOnFarm(GameLocation location)
    {
        if (location is Cellar)
            return true;

        for (GameLocation? current = location; current != null; current = current.GetParentLocation())
        {
            if (current is Farm)
                return true;
        }

        return false;
    }

    /// <summary>Find where the button fits: next to the game's own buttons if there's room, anywhere free otherwise.</summary>
    /// <remarks>
    /// The chest menu's layout changes with the chest's size, the buttons it offers and the window size, so the
    /// candidates are checked against everything the menu actually drew, and against the screen edges.
    /// </remarks>
    private static Rectangle? FindButton(ItemGrabMenu menu)
    {
        List<Rectangle> taken = new();
        foreach (ClickableComponent? component in new ClickableComponent?[] { menu.fillStacksButton, menu.organizeButton, menu.colorPickerToggleButton, menu.specialButton, menu.junimoNoteIcon, menu.trashCan, menu.okButton, menu.upperRightCloseButton })
        {
            if (component != null)
                taken.Add(component.bounds);
        }

        foreach (InventoryMenu? grid in new[] { menu.ItemsToGrabMenu, menu.inventory })
        {
            if (grid != null)
                taken.Add(new Rectangle(grid.xPositionOnScreen, grid.yPositionOnScreen, grid.width, grid.height));
        }

        if (menu.chestColorPicker is { visible: true } picker)
            taken.Add(new Rectangle(picker.xPositionOnScreen, picker.yPositionOnScreen, picker.width, picker.height));

        // the chest name sign sits under the confirm button
        if (menu.okButton != null)
            taken.Add(new Rectangle(menu.okButton.bounds.X, menu.okButton.bounds.Bottom, 64, 72));

        List<Rectangle> candidates = new();
        if (menu.fillStacksButton != null)
            candidates.Add(new Rectangle(menu.fillStacksButton.bounds.Right + 4, menu.fillStacksButton.bounds.Y, 64, 64));
        if (menu.organizeButton != null)
            candidates.Add(new Rectangle(menu.organizeButton.bounds.X, menu.organizeButton.bounds.Bottom + 4, 64, 64));
        if (menu.okButton != null)
            candidates.Add(new Rectangle(menu.okButton.bounds.X, menu.okButton.bounds.Bottom + 76, 64, 64));
        if (menu.ItemsToGrabMenu != null)
            candidates.Add(new Rectangle(menu.ItemsToGrabMenu.xPositionOnScreen - 96, menu.ItemsToGrabMenu.yPositionOnScreen + 64, 64, 64));

        Rectangle screen = new(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
        foreach (Rectangle candidate in candidates)
        {
            if (screen.Contains(candidate) && !taken.Any(area => area.Intersects(candidate)))
                return candidate;
        }

        return null;
    }

    /// <summary>Draw the button like the game's own buttons beside it, growing slightly on hover.</summary>
    private static void DrawButton(SpriteBatch b, Rectangle area)
    {
        Texture2D texture = GetButtonTexture();
        Button ??= new ClickableTextureComponent(area, texture, new Rectangle(0, 0, 16, 16), 4f);
        Button.bounds = area;
        Button.texture = texture;
        Button.tryHover(Game1.getOldMouseX(), Game1.getOldMouseY());
        Button.draw(b);
    }

    /// <summary>Build the button's picture: the frame of the game's "add to existing stacks" button, with a chest inside.</summary>
    /// <remarks>Taken from the game's sprites rather than drawn by hand, so it matches them — texture packs included.</remarks>
    private static Texture2D GetButtonTexture()
    {
        if (ButtonTexture is { IsDisposed: false })
            return ButtonTexture;

        Color[] pixels = new Color[16 * 16];
        Game1.mouseCursors.GetData(0, FrameSource, pixels, 0, pixels.Length);

        // clear the original icon down to the button's own background colour
        Color background = pixels[2 * 16 + 2];
        for (int y = 2; y <= 14; y++)
        {
            for (int x = 2; x <= 13; x++)
                pixels[y * 16 + x] = background;
        }

        // then place the small chest in the middle
        Color[] chest = new Color[ChestIconSource.Width * ChestIconSource.Height];
        Game1.mouseCursors.GetData(0, ChestIconSource, chest, 0, chest.Length);
        for (int y = 0; y < ChestIconSource.Height; y++)
        {
            for (int x = 0; x < ChestIconSource.Width; x++)
            {
                Color pixel = chest[y * ChestIconSource.Width + x];
                if (pixel.A > 0)
                    pixels[(y + 3) * 16 + (x + 3)] = pixel;
            }
        }

        ButtonTexture = new Texture2D(Game1.graphics.GraphicsDevice, 16, 16);
        ButtonTexture.SetData(pixels);
        return ButtonTexture;
    }
}
