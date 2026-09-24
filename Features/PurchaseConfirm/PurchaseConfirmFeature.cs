using System;
using HarmonyLib;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.PurchaseConfirm;

/// <summary>Asks how many to buy before any shop purchase, so nothing is bought by accident.</summary>
/// <remarks>
/// Buying in a shop is a single click, which costs real money and can't be undone. This puts the game's own quantity
/// window in between: type the number and press Enter, with the total shown and a Cancel button. It also replaces
/// shift-clicking to buy several.
/// </remarks>
internal class PurchaseConfirmFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static PurchaseConfirmFeature? Instance;

    /// <summary>Whether a purchase is being made, so our own clicks aren't intercepted again.</summary>
    private bool IsPurchasing;

    /// <summary>The shop to return to once the quantity window closes.</summary>
    private ShopMenu? ShopToRestore;


    /*********
    ** Public methods
    *********/
    public override string Id => "purchase-confirm";

    public PurchaseConfirmFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;

        this.Prefix(
            AccessTools.Method(typeof(ShopMenu), nameof(ShopMenu.receiveLeftClick)),
            typeof(PurchaseConfirmFeature),
            nameof(Before_ReceiveLeftClick)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        this.ShopToRestore = null;
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Intercept a click on something for sale, and ask for the quantity first.</summary>
    /// <returns>Returns whether to run the game's own click handling.</returns>
    private static bool Before_ReceiveLeftClick(ShopMenu __instance, int x, int y)
    {
        if (Instance is null || Instance.IsPurchasing || __instance.readOnly || __instance.heldItem != null)
            return true;

        try
        {
            for (int i = 0; i < __instance.forSaleButtons.Count; i++)
            {
                int index = __instance.currentItemIndex + i;
                if (index >= __instance.forSale.Count || !__instance.forSaleButtons[i].containsPoint(x, y))
                    continue;

                ISalable? item = __instance.forSale[index];
                if (item is null || !__instance.itemPriceAndStock.ContainsKey(item))
                    return true;

                Instance.AskQuantity(__instance, item, x, y);
                return false; // the purchase happens once the player confirms
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to ask for a purchase quantity:\n{ex}", LogLevel.Error);
        }

        return true;
    }

    /// <summary>Open the game's quantity window for an item.</summary>
    private void AskQuantity(ShopMenu shop, ISalable item, int x, int y)
    {
        ItemStockInformation stock = shop.itemPriceAndStock[item];
        int maximum = this.GetMaximum(shop, item, stock);

        this.ShopToRestore = shop;
        Game1.activeClickableMenu = QuantityPrompt.Create(
            message: this.Helper.Translation.Get("shop.quantity", new { item = item.DisplayName }),
            onChosen: quantity => this.Purchase(shop, item, quantity, x, y),
            maximum: maximum,
            price: stock.Price
        );
    }

    /// <summary>Get how many of an item the player could buy at most.</summary>
    private int GetMaximum(ShopMenu shop, ISalable item, ItemStockInformation stock)
    {
        int maximum = item.GetSalableInstance().maximumStackSize();
        if (maximum <= 0)
            maximum = 1;

        if (stock.Stock != int.MaxValue && !item.IsInfiniteStock())
            maximum = Math.Min(maximum, stock.Stock);

        if (stock.Price > 0)
        {
            int affordable = ShopMenu.getPlayerCurrencyAmount(Game1.player, shop.currency) / stock.Price;
            maximum = Math.Min(maximum, affordable);
        }

        return Math.Max(1, maximum);
    }

    /// <summary>Buy the chosen quantity, then put it straight into the bag.</summary>
    private void Purchase(ShopMenu shop, ISalable item, int quantity, int x, int y)
    {
        this.IsPurchasing = true;
        try
        {
            bool soldOut = this.Helper.Reflection
                .GetMethod(shop, "tryToPurchaseItem")
                .Invoke<bool>(item, shop.heldItem, quantity, x, y);

            if (soldOut)
            {
                shop.itemPriceAndStock.Remove(item);
                shop.forSale.Remove(item);
            }

            // the game normally leaves the purchase on the cursor; here the player already confirmed, so it goes in the bag
            if (shop.heldItem is Item held && Game1.player.addItemToInventoryBool(held))
            {
                shop.heldItem = null;
                DelayedAction.playSoundAfterDelay("coin", 100);
            }

            shop.updateSaleButtonNeighbors();

            // the game's quantity window doesn't close itself: put the shop back so the player lands on the item list
            this.ShopToRestore = null;
            Game1.activeClickableMenu = shop;
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to buy the chosen quantity:\n{ex}", LogLevel.Error);

            // whatever happened, don't leave the player stuck in the quantity window
            this.ShopToRestore = null;
            Game1.activeClickableMenu = shop;
        }
        finally
        {
            this.IsPurchasing = false;
        }
    }

    /// <summary>Put the shop back once the quantity window closes, whether the player confirmed or cancelled.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (this.ShopToRestore is null || e.OldMenu is not NumberSelectionMenu || e.NewMenu != null)
            return;

        Game1.activeClickableMenu = this.ShopToRestore;
        this.ShopToRestore = null;
    }
}
