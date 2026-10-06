using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;

namespace PelicanMemory.Features.PhoneOrders;

/// <summary>Order a tool upgrade from Clint, or a house upgrade or building from Robin, by phone, with materials from the farm's chests.</summary>
/// <remarks>
/// In multiplayer the clock never stops, and the blacksmith and the carpenter keep short hours. The phone already
/// shows their prices, read-only; here, when the shop really answers, the order can be placed from there. The game's
/// own purchase code does everything (prices, the old tool handed over, the building placed, Robin and Clint's lines):
/// the mod only unlocks the menu and lets the payment also reach the farm's chests. The phone is in the house, so the
/// player is on the farm.
///
/// Rules kept from the game: only when the phone says the shop is open (not the answering machine, not "busy"); a
/// tool still at the blacksmith's, even finished, must be picked up first; nothing while the house is being upgraded;
/// the upgraded tool is still collected at the forge. Robin's shop of wood and furniture stays read-only.
/// </remarks>
internal class PhoneOrdersFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static PhoneOrdersFeature? Instance;

    private const string ClintShopId = "ClintUpgrade";
    private const string ClintOpenKey = "Strings\\Characters:Phone_Clint_Open";
    private const string RobinOpenKey = "Strings\\Characters:Phone_Robin_Open";

    /// <summary>The time each shop stops answering, as the phone itself decides.</summary>
    private const int ClintCloses = 1600;
    private const int RobinCloses = 1700;

    private readonly FarmStock Stock;

    /// <summary>Whether the last call to each shop got a real answer, as the game showed it.</summary>
    private bool ClintAnswered;
    private bool RobinAnswered;

    /// <summary>The menus opened by phone and unlocked for ordering.</summary>
    private readonly ConditionalWeakTable<IClickableMenu, object> PhoneMenus = new();

    /// <summary>Whether a house upgrade is being ordered by phone.</summary>
    private bool HouseOrder;

    /// <summary>How deep the game is in a phone payment: while it is, the player's bag also counts the farm's chests.</summary>
    private static int PaymentDepth;


    /*********
    ** Public methods
    *********/
    public override string Id => "phone-orders";

    public PhoneOrdersFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, StorageIndex storage)
        : base(helper, monitor, harmony, settings)
    {
        this.Stock = new FarmStock(storage);
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        this.Helper.Events.Player.Warped += this.OnWarped;

        Type self = typeof(PhoneOrdersFeature);

        // what the phone answered
        this.Prefix(AccessTools.Method(typeof(DefaultPhoneHandler), nameof(DefaultPhoneHandler.CallBlacksmith)), self, nameof(Before_CallBlacksmith));
        this.Prefix(AccessTools.Method(typeof(DefaultPhoneHandler), nameof(DefaultPhoneHandler.CallCarpenter)), self, nameof(Before_CallCarpenter));
        this.Prefix(AccessTools.Method(typeof(Game1), nameof(Game1.DrawDialogue), new[] { typeof(NPC), typeof(string) }), self, nameof(Before_DrawDialogue));
        this.Prefix(AccessTools.Method(typeof(Game1), nameof(Game1.DrawDialogue), new[] { typeof(NPC), typeof(string), typeof(object[]) }), self, nameof(Before_DrawDialogue));

        // the choices on the phone
        this.Prefix(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.answerDialogueAction)), self, nameof(Before_AnswerDialogueAction));
        this.Postfix(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.answerDialogueAction)), self, nameof(After_AnswerDialogueAction));

        // the payment: while the game checks or takes materials for a phone order, the farm's chests count too
        foreach (string method in new[] { nameof(ShopMenu.HasTradeItem), nameof(ShopMenu.ConsumeTradeItem) })
        {
            this.Prefix(AccessTools.Method(typeof(ShopMenu), method), self, nameof(Before_PhonePayment));
            this.Finalizer(AccessTools.Method(typeof(ShopMenu), method), self, nameof(After_PhonePayment));
        }
        foreach (string method in new[] { nameof(CarpenterMenu.DoesFarmerHaveEnoughResourcesToBuild), nameof(CarpenterMenu.ConsumeResources), nameof(CarpenterMenu.draw) })
        {
            this.Prefix(AccessTools.Method(typeof(CarpenterMenu), method, method == nameof(CarpenterMenu.draw) ? new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) } : Type.EmptyTypes), self, nameof(Before_PhonePayment));
            this.Finalizer(AccessTools.Method(typeof(CarpenterMenu), method, method == nameof(CarpenterMenu.draw) ? new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch) } : Type.EmptyTypes), self, nameof(After_PhonePayment));
        }
        this.Prefix(AccessTools.Method(typeof(GameLocation), "houseUpgradeAccept"), self, nameof(Before_HouseUpgradeAccept));
        this.Finalizer(AccessTools.Method(typeof(GameLocation), "houseUpgradeAccept"), self, nameof(After_HouseUpgradeAccept));
        this.Postfix(AccessTools.Method(typeof(Inventory), nameof(Inventory.ContainsId), new[] { typeof(string), typeof(int) }), self, nameof(After_ContainsId));
        this.Postfix(AccessTools.Method(typeof(Inventory), nameof(Inventory.ReduceId), new[] { typeof(string), typeof(int) }), self, nameof(After_ReduceId));

        // in multiplayer, the chests can change between choosing a building and placing it: check again
        this.Prefix(AccessTools.Method(typeof(CarpenterMenu), nameof(CarpenterMenu.tryToBuild)), self, nameof(Before_TryToBuild));
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayEnding -= this.OnDayEnding;
        this.Helper.Events.Player.Warped -= this.OnWarped;
        this.Forget();
        Instance = null;
    }


    /*********
    ** Patches: what the phone answered
    *********/
    private static void Before_CallBlacksmith()
    {
        if (Instance != null)
            Instance.ClintAnswered = false;
    }

    private static void Before_CallCarpenter()
    {
        if (Instance != null)
            Instance.RobinAnswered = false;
    }

    /// <summary>Note when the shop picks up and says it's open: the game's own verdict, not one worked out again.</summary>
    private static void Before_DrawDialogue(string translationKey)
    {
        if (Instance is null || translationKey is null)
            return;

        if (translationKey.StartsWith(ClintOpenKey, StringComparison.Ordinal))
            Instance.ClintAnswered = true;
        else if (translationKey.StartsWith(RobinOpenKey, StringComparison.Ordinal))
            Instance.RobinAnswered = true;
    }


    /*********
    ** Patches: the choices on the phone
    *********/
    /// <summary>"How much for a house upgrade?" becomes Robin's own offer, when she answered.</summary>
    /// <returns>Returns whether to run the game's own code.</returns>
    private static bool Before_AnswerDialogueAction(GameLocation __instance, string questionAndAnswer, ref bool __result)
    {
        if (Instance is null)
            return true;

        try
        {
            if (questionAndAnswer == "upgrade_No")
                Instance.HouseOrder = false;

            if (questionAndAnswer != "telephone_Carpenter_HouseCost" || !Instance.CanOrderFromRobin())
                return true;

            Instance.HouseOrder = true;
            __result = __instance.answerDialogueAction("carpenter_Upgrade", Array.Empty<string>());
            return false;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to order a house upgrade by phone:\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Unlock the prices the phone just showed, when the shop answered.</summary>
    private static void After_AnswerDialogueAction(string questionAndAnswer)
    {
        if (Instance is null)
            return;

        try
        {
            switch (questionAndAnswer)
            {
                case "telephone_Blacksmith_UpgradeCost" when Game1.activeClickableMenu is ShopMenu { ShopId: ClintShopId } shop:
                    if (Instance.CanOrderFromClint())
                    {
                        shop.readOnly = false;
                        Instance.PhoneMenus.AddOrUpdate(shop, true);
                    }
                    break;

                case "telephone_Carpenter_BuildingCost" when Game1.activeClickableMenu is CarpenterMenu carpenter:
                    if (Instance.CanOrderFromRobin())
                    {
                        carpenter.readOnly = false; // the game rebuilds its buttons
                        Instance.PhoneMenus.AddOrUpdate(carpenter, true);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to open an order by phone:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Patches: the payment
    *********/
    /// <summary>A phone order is checking or taking its materials: let the farm's chests count.</summary>
    private static void Before_PhonePayment(IClickableMenu __instance, out bool __state)
    {
        __state = Instance != null && Instance.PhoneMenus.TryGetValue(__instance, out _);
        if (__state)
            PaymentDepth++;
    }

    private static void After_PhonePayment(bool __state)
    {
        if (__state)
            PaymentDepth--;
    }

    private static void Before_HouseUpgradeAccept(out bool __state)
    {
        __state = Instance?.HouseOrder == true;
        if (__state)
            PaymentDepth++;
    }

    private static void After_HouseUpgradeAccept(bool __state)
    {
        if (!__state)
            return;

        PaymentDepth--;
        if (Instance != null)
            Instance.HouseOrder = false;
    }

    /// <summary>During a phone payment, the player has an item if their bag and the farm's chests hold enough together.</summary>
    private static void After_ContainsId(Inventory __instance, string itemId, int minimum, ref bool __result)
    {
        if (__result || PaymentDepth <= 0 || Instance is null || !ReferenceEquals(__instance, Game1.player.Items))
            return;

        try
        {
            __result = __instance.CountId(itemId) + Instance.Stock.Count(itemId) >= minimum;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to count the farm's chests for a phone order:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>During a phone payment, what the bag couldn't pay is taken from the farm's chests.</summary>
    private static void After_ReduceId(Inventory __instance, string itemId, int count, ref int __result)
    {
        if (__result >= count || PaymentDepth <= 0 || Instance is null || !ReferenceEquals(__instance, Game1.player.Items))
            return;

        try
        {
            __result += Instance.Stock.Take(itemId, count - __result);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to take materials from the farm's chests for a phone order:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Check the materials are still there when the building is placed, since the other player may have used them meanwhile.</summary>
    /// <returns>Returns whether to run the game's own code.</returns>
    private static bool Before_TryToBuild(CarpenterMenu __instance, ref bool __result)
    {
        if (Instance is null || !Instance.PhoneMenus.TryGetValue(__instance, out _))
            return true;

        if (__instance.DoesFarmerHaveEnoughResourcesToBuild())
            return true;

        __result = false; // the game says it can't be built here
        return false;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get whether Clint can take an order now: he answered, he's still open, and no tool of the player's is at the forge.</summary>
    private bool CanOrderFromClint()
    {
        if (!this.ClintAnswered || !this.IsShopHours(ClintCloses))
            return false;

        Farmer player = Game1.player;
        if (player.toolBeingUpgraded.Value != null)
        {
            // even finished: it must be picked up first, or the old tool would be kept as well
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("phone-orders.tool-waiting"), HUDMessage.error_type));
            return false;
        }

        return player.daysLeftForToolUpgrade.Value <= 0;
    }

    /// <summary>Get whether Robin can take an order now: she answered, she's still open, and she isn't already building for the player.</summary>
    private bool CanOrderFromRobin()
    {
        if (!this.RobinAnswered || !this.IsShopHours(RobinCloses))
            return false;

        // the phone doesn't know, but in person Robin won't take a new job while the house is being upgraded
        if (Game1.player.daysUntilHouseUpgrade.Value >= 0)
        {
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("phone-orders.house-busy"), HUDMessage.error_type));
            return false;
        }

        return !Game1.IsThereABuildingUnderConstruction()
            && Game1.RequireLocation<Town>("Town").daysUntilCommunityUpgrade.Value <= 0;
    }

    /// <summary>Get whether a shop is still open: the clock keeps running while the phone talks, and festivals close shops.</summary>
    private bool IsShopHours(int closingTime)
    {
        return Game1.timeOfDay < closingTime
            && !GameLocation.AreStoresClosedForFestival()
            && FarmPlaces.IsOnFarm(Game1.currentLocation);
    }

    /// <summary>Forget the phone's answers and orders.</summary>
    private void Forget()
    {
        this.ClintAnswered = false;
        this.RobinAnswered = false;
        this.HouseOrder = false;
        this.PhoneMenus.Clear();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.Forget();
    }

    /// <summary>A house upgrade asked by phone and never answered must not follow the player to Robin's counter.</summary>
    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.IsLocalPlayer && !FarmPlaces.IsOnFarm(e.NewLocation))
            this.Forget();
    }
}
