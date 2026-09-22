using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.FishHints;

/// <summary>Describes the fish you haven't caught through the ones you have, so the collection can be finished without a wiki.</summary>
/// <remarks>
/// Hovering an uncaught fish in the collections tab normally says nothing but "???". This adds one line per catch
/// condition, each phrased as "the same as a fish you already caught". A condition none of your catches shares stays
/// unknown, and a fish living only where you've never been says nothing at all — so you're never told about a place,
/// a season or a fish you couldn't have discovered yourself.
/// </remarks>
internal class FishHintsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static FishHintsFeature? Instance;

    /// <summary>The text the game shows for a fish which hasn't been caught.</summary>
    private const string UnknownText = "???";

    private readonly FishHintResolver Resolver = new();

    /// <summary>The hint block per fish, since the game asks again on every frame the cursor stays put.</summary>
    private readonly Dictionary<string, string> Cache = new();


    /*********
    ** Public methods
    *********/
    public override string Id => "fish-hints";

    public FishHintsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;

        this.Postfix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.performHoverAction)),
            typeof(FishHintsFeature),
            nameof(After_PerformHoverAction)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Cache.Clear();
        this.Resolver.Reset();
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Replace the bare "???" of an uncaught fish with what the player's own catches can say about it.</summary>
    private static void After_PerformHoverAction(CollectionsPage __instance, int x, int y)
    {
        if (Instance is null || !Context.IsWorldReady || __instance.currentTab != CollectionsPage.fishTab)
            return;

        try
        {
            IReflectedField<string> hoverText = Instance.Helper.Reflection.GetField<string>(__instance, "hoverText");
            if (hoverText.GetValue() != UnknownText)
                return;

            string? fishId = GetHoveredFishId(__instance, x, y);
            if (fishId != null)
                hoverText.SetValue(Instance.GetHintText(fishId));
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to describe an uncaught fish:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the qualified ID of the fish under the cursor, or <c>null</c> if it isn't an uncaught one.</summary>
    private static string? GetHoveredFishId(CollectionsPage page, int x, int y)
    {
        if (!page.collections.TryGetValue(page.currentTab, out List<List<ClickableTextureComponent>>? pages) || page.currentPage >= pages.Count)
            return null;

        foreach (ClickableTextureComponent component in pages[page.currentPage])
        {
            if (!component.containsPoint(x, y, 2))
                continue;

            // the slot's name is "<itemId> <caught> <flag>"
            string[] parts = ArgUtility.SplitBySpace(component.name);
            return parts.Length > 0 ? ItemRegistry.type_object + parts[0] : null;
        }

        return null;
    }

    /// <summary>Build the tooltip for an uncaught fish.</summary>
    private string GetHintText(string qualifiedItemId)
    {
        if (this.Cache.TryGetValue(qualifiedItemId, out string? cached))
            return cached;

        StringBuilder text = new(UnknownText);
        Func<string, bool> hasVisited = Game1.player.locationsVisited.Contains;

        if (!this.Resolver.IsReachable(qualifiedItemId, hasVisited))
            text.Append('\n').Append(this.Helper.Translation.Get("fish-hints.unreachable"));
        else
        {
            IReadOnlyList<FishHint> hints = this.Resolver.GetHints(qualifiedItemId, hasVisited, Game1.player.fishCaught.Keys.ToArray());
            if (hints.Count > 0)
            {
                text.Append('\n').Append(this.Helper.Translation.Get("fish-hints.title"));
                foreach (FishHint hint in hints)
                    text.Append('\n').Append(this.Helper.Translation.Get($"fish-hints.axis.{hint.Axis}")).Append(" : ").Append(this.GetValueText(hint));
            }
        }

        return this.Cache[qualifiedItemId] = text.ToString();
    }

    /// <summary>Get the right-hand side of a hint line.</summary>
    private string GetValueText(FishHint hint)
    {
        if (hint.PlainKey != null)
            return this.Helper.Translation.Get($"fish-hints.{hint.PlainKey}");

        return hint.CousinId != null
            ? this.Helper.Translation.Get("fish-hints.like", new { fish = ItemRegistry.GetDataOrErrorItem(hint.CousinId).DisplayName })
            : this.Helper.Translation.Get("fish-hints.unknown");
    }

    /// <summary>Forget the cached hints, since a night of fishing changes what can be compared.</summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Cache.Clear();
        this.Resolver.Reset();
    }
}
