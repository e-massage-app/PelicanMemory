using System;
using System.Collections.Generic;
using HarmonyLib;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace PelicanMemory.Features.TransferQuantity;

/// <summary>Ctrl + click on a stack in a chest or fridge to move an exact number of it to the other side.</summary>
/// <remarks>
/// The game only moves a whole stack, half of one, or a single item. This asks for the number instead, with the game's
/// own quantity window, and moves exactly that many — from the chest to the bag or the other way round. Normal clicks
/// are left alone, and a stack of one just behaves as usual.
/// </remarks>
internal class TransferQuantityFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static TransferQuantityFeature? Instance;

    /// <summary>The config key for the modifier key.</summary>
    private const string KeybindKey = "transfer-quantity";

    /// <summary>The default modifier: either Ctrl key.</summary>
    private const string DefaultKeybind = "LeftControl, RightControl";

    /// <summary>The chest window to go back to once the quantity window closes.</summary>
    private ItemGrabMenu? MenuToRestore;


    /*********
    ** Public methods
    *********/
    public override string Id => "transfer-quantity";

    public TransferQuantityFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;

        this.Prefix(
            AccessTools.Method(typeof(ItemGrabMenu), nameof(ItemGrabMenu.receiveLeftClick)),
            typeof(TransferQuantityFeature),
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
    /// <summary>Ask for a quantity when a stack is Ctrl + clicked.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_ReceiveLeftClick(ItemGrabMenu __instance, int x, int y)
    {
        // only real containers: not the shipping bin, fishing treasure or other one-way menus
        if (Instance is null || __instance.source != ItemGrabMenu.source_chest || __instance.heldItem != null)
            return true;

        KeybindList modifier = Instance.Settings.GetKeybind(KeybindKey, DefaultKeybind);
        if (!modifier.IsDown())
            return true;

        try
        {
            if (TryGetStack(__instance.ItemsToGrabMenu, x, y, out int index, out Item? item))
            {
                Instance.Ask(__instance, fromContainer: true, index, item);
                return false;
            }

            if (TryGetStack(__instance.inventory, x, y, out index, out item))
            {
                Instance.Ask(__instance, fromContainer: false, index, item);
                return false;
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to ask for a quantity to move:\n{ex}", LogLevel.Error);
        }

        return true;
    }

    /// <summary>Get the stack under the cursor in one of the two grids, if it holds more than one item.</summary>
    private static bool TryGetStack(InventoryMenu? grid, int x, int y, out int index, out Item item)
    {
        index = grid?.getInventoryPositionOfClick(x, y) ?? -1;
        item = null!;

        if (grid is null || index < 0 || index >= grid.actualInventory.Count || grid.actualInventory[index] is not Item found)
            return false;

        // a single item has nothing to choose: the normal click moves it
        if (found.Stack <= 1)
            return false;

        item = found;
        return true;
    }

    /// <summary>Open the quantity window for a stack.</summary>
    private void Ask(ItemGrabMenu menu, bool fromContainer, int index, Item item)
    {
        Game1.playSound("smallSelect");
        this.MenuToRestore = menu;

        string question = this.Helper.Translation.Get(fromContainer ? "transfer.take" : "transfer.store", new { item = item.DisplayName, count = item.Stack });
        Game1.activeClickableMenu = QuantityPrompt.Create(
            message: question,
            onChosen: quantity => this.Move(menu, fromContainer, index, item, quantity),
            maximum: item.Stack
        );
    }

    /// <summary>Move an exact number of items to the other side, as many as fit.</summary>
    private void Move(ItemGrabMenu menu, bool fromContainer, int index, Item source, int quantity)
    {
        try
        {
            IList<Item> from = fromContainer ? menu.ItemsToGrabMenu.actualInventory : menu.inventory.actualInventory;

            // the stack may have changed while the window was open (the other player, a hopper): only move it if it's still there
            if (index >= from.Count || !ReferenceEquals(from[index], source))
            {
                Game1.playSound("cancel");
                return;
            }

            int requested = Math.Min(quantity, source.Stack);
            Item moving = source.getOne();
            moving.Stack = requested;

            // the game shrinks the stack it's given to what didn't fit, and hands that same stack back as the rest,
            // so what arrived is worked out from the quantity asked for, not from the stack afterwards
            Item? rest = fromContainer
                ? Game1.player.addItemToInventory(moving)
                : AddToContainer(menu, moving);

            int moved = requested - (rest?.Stack ?? 0);
            if (moved <= 0)
            {
                Game1.playSound("cancel");
                Game1.showRedMessage(this.Helper.Translation.Get("transfer.no-room"));
                return;
            }

            // take from the source only what actually arrived on the other side
            source.Stack -= moved;
            if (source.Stack <= 0)
                from[index] = null!;

            Game1.playSound("dwop");
            if (rest != null)
                Game1.showRedMessage(this.Helper.Translation.Get("transfer.partial", new { count = moved }));
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to move the chosen quantity:\n{ex}", LogLevel.Error);
        }
        finally
        {
            // the game's quantity window doesn't close itself: go back to the chest
            this.MenuToRestore = null;
            Game1.activeClickableMenu = menu;
        }
    }

    /// <summary>Put items in the open container, stacking onto what's there first, and return what didn't fit.</summary>
    private static Item? AddToContainer(ItemGrabMenu menu, Item item)
    {
        if (menu.sourceItem is Chest chest)
            return chest.addItem(item);

        return Utility.addItemToThisInventoryList(item, menu.ItemsToGrabMenu.actualInventory, menu.ItemsToGrabMenu.capacity);
    }

    /// <summary>Go back to the chest when the quantity window is cancelled.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (this.MenuToRestore is null || e.OldMenu is not NumberSelectionMenu || e.NewMenu != null)
            return;

        Game1.activeClickableMenu = this.MenuToRestore;
        this.MenuToRestore = null;
    }
}
