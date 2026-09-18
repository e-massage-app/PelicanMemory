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
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.CommunityCenterHints;

/// <summary>Marks items needed for a community center bundle, so they aren't sold or used by mistake.</summary>
/// <remarks>
/// Anti-spoil: only bundles whose note is currently showing in the community center are considered, which is the
/// same rule the game uses for its own hint (the pulsing junimo note when hovering such an item). Nothing is shown
/// for rooms the player can't see yet, and nothing at all on the Joja route.
/// </remarks>
internal class CommunityCenterHintsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static CommunityCenterHintsFeature? Instance;

    /// <summary>A slot in a bundle which accepts an item.</summary>
    /// <param name="AreaName">The room's translated name.</param>
    /// <param name="BundleName">The bundle's translated name.</param>
    /// <param name="Quantity">How many items the slot needs.</param>
    /// <param name="Quality">The minimum quality (0 = any).</param>
    /// <param name="Donated">Whether the slot is already filled.</param>
    private record BundleSlot(string AreaName, string BundleName, int Quantity, int Quality, bool Donated);

    /// <summary>The bundle slots indexed by qualified item ID, or by category for slots which accept any item of a category.</summary>
    private Dictionary<string, List<BundleSlot>>? SlotsByItem;

    /// <summary>The generated tooltip text indexed by qualified item ID and quality.</summary>
    private readonly Dictionary<(string ItemId, int Quality), string> TextCache = new();

    /// <summary>The junimo note sprite, the same one the game pulses when an item is needed.</summary>
    private static readonly Rectangle JunimoNoteSource = new(331, 374, 15, 14);


    /*********
    ** Public methods
    *********/
    public override string Id => "community-center-hints";

    public CommunityCenterHintsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;
        this.Helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        TooltipBadges.AddProvider(this.GetTooltipBadge);

        this.Postfix(
            AccessTools.Method(typeof(SObject), nameof(SObject.getDescription)),
            typeof(CommunityCenterHintsFeature),
            nameof(After_GetDescription)
        );
        this.Postfix(
            AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(int) }),
            typeof(CommunityCenterHintsFeature),
            nameof(After_InventoryDraw)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        this.Helper.Events.GameLoop.ReturnedToTitle -= this.OnReturnedToTitle;
        TooltipBadges.RemoveProvider(this.GetTooltipBadge);

        this.ClearCache();
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    private static void After_GetDescription(SObject __instance, ref string __result)
    {
        if (Instance is null || !Context.IsWorldReady || __instance.IsRecipe || __instance.bigCraftable.Value)
            return;

        try
        {
            string? extra = Instance.GetHint(__instance);
            if (!string.IsNullOrEmpty(extra))
            {
                int width = Instance.Helper.Reflection.GetMethod(__instance, "getDescriptionWidth").Invoke<int>();
                __result = __result.TrimEnd() + "\n\n" + Game1.parseText(extra, Game1.smallFont, width);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to add the community center hint:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the hint for an item, or <c>null</c> if no visible bundle wants it.</summary>
    private string? GetHint(SObject item)
    {
        var key = (item.QualifiedItemId, item.Quality);
        if (this.TextCache.TryGetValue(key, out string? cached))
            return cached;

        string? text = this.BuildHint(item);
        this.TextCache[key] = text ?? "";
        return text;
    }

    private string? BuildHint(SObject item)
    {
        List<BundleSlot> slots = this.GetSlots(item);
        if (slots.Count == 0)
            return null;

        // needed now, and this item is good enough
        BundleSlot[] needed = slots.Where(slot => !slot.Donated && item.Quality >= slot.Quality).ToArray();
        if (needed.Length > 0)
            return this.Helper.Translation.Get("cc.needed", new { value = Describe(needed, withQuantity: true) });

        // needed, but a better quality is required
        BundleSlot[] betterQuality = slots.Where(slot => !slot.Donated && item.Quality < slot.Quality).ToArray();
        if (betterQuality.Length > 0)
            return this.Helper.Translation.Get("cc.better-quality", new { value = Describe(betterQuality, withQuantity: false) });

        // already handed in: the ticked box in the tooltip says it, no need for text
        return null;
    }

    /// <summary>Draw a badge on inventory slots: the junimo note when a bundle wants the item, a tick once it's been handed in.</summary>
    private static void After_InventoryDraw(InventoryMenu __instance, SpriteBatch b)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            SlotBadges.Draw(b, __instance, Instance.GetSlotBadge, BadgeCorner.TopRight);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the community center badges:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the inventory badge for an item: only what a bundle still needs, so the bags don't fill up with ticks.</summary>
    private Badge? GetSlotBadge(Item item)
    {
        return this.IsWanted(item)
            ? new Badge(Game1.mouseCursors, JunimoNoteSource)
            : null;
    }

    /// <summary>Get the tooltip stamp for an item, ticked once every bundle slot for it is filled.</summary>
    private TooltipBadge? GetTooltipBadge(Item item)
    {
        List<BundleSlot> slots = this.GetSlots(item);
        return slots.Count > 0
            ? new TooltipBadge(Game1.mouseCursors, JunimoNoteSource, Done: slots.All(slot => slot.Donated))
            : null;
    }

    /// <summary>Get whether a bundle still needs this item.</summary>
    private bool IsWanted(Item item)
    {
        return this.GetSlots(item).Any(slot => !slot.Donated);
    }

    /// <summary>Get the visible bundle slots which accept an item.</summary>
    private List<BundleSlot> GetSlots(Item item)
    {
        List<BundleSlot> slots = new();
        if (item is not SObject obj || obj.bigCraftable.Value)
            return slots;

        this.SlotsByItem ??= this.BuildIndex();

        if (this.SlotsByItem.TryGetValue(obj.QualifiedItemId, out List<BundleSlot>? byId))
            slots.AddRange(byId);
        if (obj.Category < 0 && this.SlotsByItem.TryGetValue(obj.Category.ToString(), out List<BundleSlot>? byCategory))
            slots.AddRange(byCategory);

        return slots;
    }

    /// <summary>List the rooms and bundles for a set of slots.</summary>
    private static string Describe(IEnumerable<BundleSlot> slots, bool withQuantity)
    {
        return string.Join(", ", slots
            .Select(slot => withQuantity && slot.Quantity > 1
                ? $"{slot.AreaName} ({slot.BundleName} x{slot.Quantity})"
                : $"{slot.AreaName} ({slot.BundleName})")
            .Distinct());
    }

    /// <summary>Index every bundle slot the player can currently see.</summary>
    private Dictionary<string, List<BundleSlot>> BuildIndex()
    {
        Dictionary<string, List<BundleSlot>> index = new(StringComparer.OrdinalIgnoreCase);

        // nothing to show on the Joja route: the bundles are gone
        if (Game1.player.hasOrWillReceiveMail("JojaMember") || Game1.getLocationFromName("CommunityCenter") is not CommunityCenter communityCenter)
            return index;

        Dictionary<int, bool[]> donated = communityCenter.bundlesDict();

        foreach ((string bundleKey, string rawBundle) in Game1.netWorldState.Value.BundleData)
        {
            string[] keyParts = bundleKey.Split('/');
            if (keyParts.Length < 2 || !int.TryParse(keyParts[1], out int bundleIndex))
                continue;

            // the game only shows a room's note once the player can work on it: same rule here, so nothing is revealed early
            int areaNumber = CommunityCenter.getAreaNumberFromName(keyParts[0]);
            if (!communityCenter.shouldNoteAppearInArea(areaNumber))
                continue;

            string[] fields = rawBundle.Split('/');
            if (fields.Length < 3)
                continue;

            string areaName = CommunityCenter.getAreaDisplayNameFromNumber(areaNumber);
            string bundleName = fields.Length > 6 && !string.IsNullOrWhiteSpace(fields[6]) ? fields[6] : fields[0];
            string[] parts = ArgUtility.SplitBySpace(fields[2]);

            for (int i = 0; i + 2 < parts.Length; i += 3)
            {
                if (!int.TryParse(parts[i + 1], out int quantity) || !int.TryParse(parts[i + 2], out int quality))
                    continue;

                bool isDonated = donated.TryGetValue(bundleIndex, out bool[]? flags) && i / 3 < flags.Length && flags[i / 3];
                string itemKey = GetItemKey(parts[i]);

                if (!index.TryGetValue(itemKey, out List<BundleSlot>? list))
                    index[itemKey] = list = new List<BundleSlot>();
                list.Add(new BundleSlot(areaName, bundleName, quantity, quality, isDonated));
            }
        }

        return index;
    }

    /// <summary>Get the index key for a bundle ingredient, which is either a category number or a qualified item ID.</summary>
    private static string GetItemKey(string rawId)
    {
        if (int.TryParse(rawId, out int numericId) && numericId < 0)
            return numericId.ToString(); // a category, e.g. -5 for eggs

        return ItemRegistry.GetData(rawId)?.QualifiedItemId ?? ItemRegistry.type_object + rawId;
    }

    private void ClearCache()
    {
        this.SlotsByItem = null;
        this.TextCache.Clear();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.ClearCache();
    }

    /// <summary>Refresh after any menu closes, since the player may have just handed items in.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        this.ClearCache();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.ClearCache();
    }
}
