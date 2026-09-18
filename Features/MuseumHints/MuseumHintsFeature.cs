using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.MuseumHints;

/// <summary>Marks minerals and artifacts the museum still wants, and ticks those already donated.</summary>
/// <remarks>
/// Anti-spoil: nothing is shown until the player has been to the museum, since that's where they learn donations
/// exist. Afterwards it only reflects their own donations.
/// </remarks>
internal class MuseumHintsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static MuseumHintsFeature? Instance;

    /// <summary>The collections tab sprites used as badges: a pot for artifacts, a gem for minerals.</summary>
    private static readonly Rectangle ArtifactSource = new(656, 64, 16, 16);
    private static readonly Rectangle MineralSource = new(672, 64, 16, 16);

    /// <summary>The museum's location name, which is also how a visit is recorded.</summary>
    private const string MuseumLocationName = "ArchaeologyHouse";

    /// <summary>The status of each item, since this is asked for every inventory slot on every frame.</summary>
    private readonly Dictionary<string, DonationStatus> StatusCache = new();

    /// <summary>The number of donated pieces when <see cref="StatusCache"/> was filled.</summary>
    private int CachedDonationCount = -1;


    /*********
    ** Public methods
    *********/
    public override string Id => "museum-hints";

    public MuseumHintsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        TooltipBadges.AddProvider(this.GetTooltipBadge);

        this.Postfix(
            AccessTools.Method(typeof(SObject), nameof(SObject.getDescription)),
            typeof(MuseumHintsFeature),
            nameof(After_GetDescription)
        );
        this.Postfix(
            AccessTools.Method(typeof(InventoryMenu), nameof(InventoryMenu.draw), new[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(int) }),
            typeof(MuseumHintsFeature),
            nameof(After_InventoryDraw)
        );
    }

    protected override void OnDisable()
    {
        TooltipBadges.RemoveProvider(this.GetTooltipBadge);
        this.StatusCache.Clear();
        this.CachedDonationCount = -1;
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Say an item is still wanted by the museum. Donated ones only get the tick on the slot.</summary>
    private static void After_GetDescription(SObject __instance, ref string __result)
    {
        if (Instance is null || !Instance.IsKnown() || __instance.IsRecipe)
            return;

        try
        {
            if (Instance.GetStatus(__instance) == DonationStatus.Wanted)
            {
                int width = Instance.Helper.Reflection.GetMethod(__instance, "getDescriptionWidth").Invoke<int>();
                string text = Instance.Helper.Translation.Get("museum.wanted");
                __result = __result.TrimEnd() + "\n\n" + Game1.parseText(text, Game1.smallFont, width);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to add the museum hint:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Draw a badge on inventory slots: the collection icon when the museum wants the item, a tick once donated.</summary>
    private static void After_InventoryDraw(InventoryMenu __instance, SpriteBatch b)
    {
        if (Instance is null || !Instance.IsKnown())
            return;

        try
        {
            SlotBadges.Draw(b, __instance, Instance.GetSlotBadge, BadgeCorner.TopLeft);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the museum badges:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Whether the player knows about museum donations, i.e. has been to the museum.</summary>
    private bool IsKnown()
    {
        return Context.IsWorldReady && Game1.player.locationsVisited.Contains(MuseumLocationName);
    }

    /// <summary>Get the inventory badge: only what the museum is still missing.</summary>
    private Badge? GetSlotBadge(Item item)
    {
        return item is SObject obj && this.GetStatus(obj) == DonationStatus.Wanted
            ? new Badge(Game1.mouseCursors, GetIcon(obj))
            : null;
    }

    /// <summary>Get the tooltip stamp, ticked once the piece is in the museum.</summary>
    private TooltipBadge? GetTooltipBadge(Item item)
    {
        if (!this.IsKnown() || item is not SObject obj)
            return null;

        return this.GetStatus(obj) switch
        {
            DonationStatus.Wanted => new TooltipBadge(Game1.mouseCursors, GetIcon(obj), Done: false),
            DonationStatus.Donated => new TooltipBadge(Game1.mouseCursors, GetIcon(obj), Done: true),
            _ => null
        };
    }

    /// <summary>Get whether the museum wants an item, already has it, or doesn't take it at all.</summary>
    private DonationStatus GetStatus(SObject item)
    {
        if (item.bigCraftable.Value)
            return DonationStatus.NotForMuseum;

        // donating changes every answer, so the cache follows the number of pieces in the museum
        int donationCount = Game1.netWorldState.Value.MuseumPieces.Length;
        if (donationCount != this.CachedDonationCount)
        {
            this.StatusCache.Clear();
            this.CachedDonationCount = donationCount;
        }

        if (this.StatusCache.TryGetValue(item.QualifiedItemId, out DonationStatus cached))
            return cached;

        DonationStatus status = !LibraryMuseum.IsItemSuitableForDonation(item.QualifiedItemId, checkDonatedItems: false)
            ? DonationStatus.NotForMuseum
            : LibraryMuseum.HasDonatedArtifact(item.QualifiedItemId)
                ? DonationStatus.Donated
                : DonationStatus.Wanted;

        this.StatusCache[item.QualifiedItemId] = status;
        return status;
    }

    /// <summary>Get the icon for an item: a pot for artifacts, a gem for minerals.</summary>
    private static Rectangle GetIcon(SObject item)
    {
        ParsedItemData? data = ItemRegistry.GetData(item.QualifiedItemId);
        return data?.ObjectType == "Arch" ? ArtifactSource : MineralSource;
    }

    private enum DonationStatus
    {
        NotForMuseum,
        Wanted,
        Donated
    }
}
