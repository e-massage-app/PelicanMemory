using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;

namespace PelicanMemory.Features.CraftFromChests;

/// <summary>On the farm, crafting and cooking also use what's in the farm's chests.</summary>
/// <remarks>
/// The game already does this in small: the workbench uses the chests next to it, the kitchen uses the fridge. Here
/// every storage chest of the farm counts, but only while the player is on the farm — crafting from a cave with the
/// farm's chests would be cheating.
///
/// Each chest is locked while the crafting page is open, exactly like the game's kitchen and workbench do, so two
/// players can never use the same items at once. The game only grants those locks for chests it keeps up to date: the
/// host (or a solo player) locks every chest of the farm; a farmhand locks the chests of the room they're in and the
/// shared Junimo chests, and uses the farm's other chests without a lock (the game can't grant one from afar), as long
/// as nobody has them open. A chest someone else has open is always left out until they close it.
/// </remarks>
internal class CraftFromChestsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static CraftFromChestsFeature? Instance;

    private readonly StorageIndex Storage;

    /// <summary>The crafting page the chests are currently given to.</summary>
    private CraftingPage? Page;

    /// <summary>The containers the game itself gave the page (fridge, chests next to a workbench), kept first and given back at the end.</summary>
    private List<IInventory>? OriginalContainers;

    /// <summary>The farm chests offered to the page, with the locks this feature asked for.</summary>
    private readonly List<Chest> Chests = new();
    private readonly List<NetMutex> OwnLocks = new();

    /// <summary>For a farmhand, the farm chests outside their room, used while nobody has them open.</summary>
    private readonly List<Chest> UnlockedChests = new();

    /// <summary>What the page sees in its containers, worked out once per tick: the page asks for it once per recipe per frame.</summary>
    private IList<Item>? CachedContents;


    /*********
    ** Public methods
    *********/
    public override string Id => "craft-from-chests";

    public CraftFromChestsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, StorageIndex storage)
        : base(helper, monitor, harmony, settings)
    {
        this.Storage = storage;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.UpdateTicking += this.OnUpdateTicking;
        this.Helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        this.Helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;

        this.Prefix(AccessTools.Method(typeof(CraftingPage), "getContainerContents"), typeof(CraftFromChestsFeature), nameof(Before_GetContainerContents));
        this.Postfix(AccessTools.Method(typeof(CraftingPage), "getContainerContents"), typeof(CraftFromChestsFeature), nameof(After_GetContainerContents));
        this.Postfix(AccessTools.Method(typeof(CraftingRecipe), nameof(CraftingRecipe.consumeIngredients)), typeof(CraftFromChestsFeature), nameof(After_Consume));
        this.Postfix(AccessTools.Method(typeof(CraftingRecipe), nameof(CraftingRecipe.ConsumeAdditionalIngredients)), typeof(CraftFromChestsFeature), nameof(After_Consume));
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.UpdateTicking -= this.OnUpdateTicking;
        this.Helper.Events.GameLoop.DayEnding -= this.OnDayEnding;
        this.Helper.Events.GameLoop.ReturnedToTitle -= this.OnReturnedToTitle;
        this.EndSession();
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Give the page what was already worked out this tick, rather than gathering every chest again for each recipe.</summary>
    /// <returns>Returns whether to run the game's own code.</returns>
    private static bool Before_GetContainerContents(CraftingPage __instance, ref IList<Item> __result)
    {
        if (Instance?.Page is null || !ReferenceEquals(__instance, Instance.Page) || Instance.CachedContents is null)
            return true;

        __result = Instance.CachedContents;
        return false;
    }

    /// <summary>Keep what the game gathered, for the rest of the tick.</summary>
    private static void After_GetContainerContents(CraftingPage __instance, IList<Item> __result)
    {
        if (Instance?.Page != null && ReferenceEquals(__instance, Instance.Page))
            Instance.CachedContents = __result;
    }

    /// <summary>Something was made: what's left must be counted again, even within the same tick (shift-click makes several).</summary>
    private static void After_Consume()
    {
        if (Instance != null)
            Instance.CachedContents = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Follow the crafting page on screen: open a session when one appears on the farm, close it when it goes.</summary>
    private void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
    {
        try
        {
            this.CachedContents = null;

            CraftingPage? target = Context.IsWorldReady && FarmPlaces.IsOnFarm(Game1.currentLocation) ? GetCraftingPage() : null;
            if (!ReferenceEquals(target, this.Page))
            {
                this.EndSession();
                if (target != null)
                    this.StartSession(target);
            }

            if (this.Page != null)
                this.Page._materialContainers = this.GetContainers();
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to give the farm's chests to the crafting page:\n{ex}", LogLevel.Error);
            this.EndSession();
        }
    }

    /// <summary>Get the crafting or cooking page on screen, if any.</summary>
    private static CraftingPage? GetCraftingPage()
    {
        return Game1.activeClickableMenu switch
        {
            GameMenu menu => menu.GetCurrentPage() as CraftingPage,
            CraftingPage page => page, // the kitchen and the workbench
            _ => null
        };
    }

    /// <summary>Start giving the farm's chests to a page, locking each one like the game's kitchen does.</summary>
    private void StartSession(CraftingPage page)
    {
        this.Page = page;
        this.OriginalContainers = page._materialContainers;

        HashSet<object> alreadyThere = new(this.OriginalContainers ?? new List<IInventory>(), ReferenceEqualityComparer.Instance);
        GameLocation here = Game1.currentLocation;

        foreach (Chest chest in FarmChests.Get())
        {
            // the kitchen's fridge, or the chests next to a workbench: the page has them already
            if (alreadyThere.Contains(chest.GetItemsForPlayer()))
                continue;

            // a farmhand can only lock the chests of their own room and the shared Junimo chests
            if (!Context.IsMainPlayer && chest.SpecialChestType != Chest.SpecialChestTypes.JunimoChest && !ReferenceEquals(chest.Location, here))
            {
                this.UnlockedChests.Add(chest);
                continue;
            }

            NetMutex mutex = chest.GetMutex();
            if (mutex.IsLocked())
                continue;

            // a farmhand's lock arrives later: if the page is gone by then, give it straight back
            mutex.RequestLock(acquired: () =>
            {
                if (!this.OwnLocks.Contains(mutex))
                    mutex.ReleaseLock();
            });
            this.Chests.Add(chest);
            this.OwnLocks.Add(mutex);
        }
    }

    /// <summary>Get the page's containers: the game's own first, then the farm chests whose lock is held.</summary>
    private List<IInventory> GetContainers()
    {
        List<IInventory> containers = new(this.OriginalContainers ?? new List<IInventory>());
        foreach (Chest chest in this.Chests)
        {
            // a lock can be lost (the host gives it back when the other player walks in): that chest stops counting
            if (chest.GetMutex().IsLockHeld())
                containers.Add(chest.GetItemsForPlayer());
        }
        foreach (Chest chest in this.UnlockedChests)
        {
            if (!chest.GetMutex().IsLocked())
                containers.Add(chest.GetItemsForPlayer());
        }
        return containers;
    }

    /// <summary>Give the page back its own containers and release every lock this feature took.</summary>
    private void EndSession()
    {
        if (this.Page is null)
            return;

        foreach (NetMutex mutex in this.OwnLocks)
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
        }

        this.Page._materialContainers = this.OriginalContainers!;
        this.Page = null;
        this.OriginalContainers = null;
        this.Chests.Clear();
        this.OwnLocks.Clear();
        this.UnlockedChests.Clear();
        this.CachedContents = null;

        // what was used came out of the chests: the other features must count again
        this.Storage.Invalidate();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.EndSession();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.EndSession();
    }
}
