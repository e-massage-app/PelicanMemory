using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Monsters;

namespace PelicanMemory.Features.CollectionHints;

/// <summary>Says where an undiscovered mineral or artifact can come from, and whether a geode still has anything new for the collection.</summary>
/// <remarks>
/// Finishing the museum by chance is near impossible without a wiki. Hovering a missing mineral or artifact in the
/// collections tab lists where it comes from (a geode, artifact spots, a monster, the mine...), and a geode's tooltip
/// says how many new items it can still give, or that it's exhausted.
///
/// Anti-spoil, like the fish hints: the item itself stays a silhouette, and a source the player has never met is never
/// named — "a geode you haven't had yet", "a place you've never visited", "a monster you haven't beaten yet". A few
/// one-off sources (secret notes, unique events) stay unknown.
/// </remarks>
internal class CollectionHintsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static CollectionHintsFeature? Instance;

    /// <summary>The text the game shows for an item not found yet.</summary>
    private const string UnknownText = "???";

    /// <summary>The most names listed on one line before the rest are counted.</summary>
    private const int NamesPerLine = 4;

    /// <summary>The mineral icon on the game's cursor sheet, for the geode badge.</summary>
    private static readonly Rectangle MineralIcon = new(672, 64, 16, 16);

    /// <summary>The special sources which are no secret, and named as soon as the player could know them.</summary>
    private static readonly HashSet<string> OpenSpecials = new() { "garbageCan", "prizeMachine", "slimeBall", "mineBarrel", "mineDirt", "mineFloor", "treasureChest", "craneGame", "mail" };

    private readonly PlayerStore Store;
    private readonly StorageIndex Storage;

    /// <summary>The hint block per item, since the game asks again on every frame the cursor stays put.</summary>
    private readonly Dictionary<string, string> Cache = new();


    /*********
    ** Public methods
    *********/
    public override string Id => "collection-hints";

    public CollectionHintsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store, StorageIndex storage)
        : base(helper, monitor, harmony, settings)
    {
        this.Store = store;
        this.Storage = storage;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Helper.Events.Display.MenuChanged += this.OnMenuChanged;
        TooltipBadges.AddProvider(this.GetGeodeBadge);

        this.Postfix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.performHoverAction)),
            typeof(CollectionHintsFeature),
            nameof(After_PerformHoverAction)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Helper.Events.Display.MenuChanged -= this.OnMenuChanged;
        TooltipBadges.RemoveProvider(this.GetGeodeBadge);
        this.Cache.Clear();
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Replace the bare "???" of a missing mineral or artifact with where it can come from.</summary>
    private static void After_PerformHoverAction(CollectionsPage __instance, int x, int y)
    {
        if (Instance is null || !Context.IsWorldReady || __instance.currentTab is not (CollectionsPage.mineralsTab or CollectionsPage.archaeologyTab))
            return;

        try
        {
            IReflectedField<string> hoverText = Instance.Helper.Reflection.GetField<string>(__instance, "hoverText");
            if (hoverText.GetValue() != UnknownText)
                return;

            if (GetHoveredItemId(__instance, x, y) is string itemId)
                hoverText.SetValue(Instance.GetHintText(itemId));
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to describe a missing museum item:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the unqualified ID of the item under the cursor in the collections tab.</summary>
    private static string? GetHoveredItemId(CollectionsPage page, int x, int y)
    {
        if (!page.collections.TryGetValue(page.currentTab, out List<List<ClickableTextureComponent>>? pages) || page.currentPage >= pages.Count)
            return null;

        foreach (ClickableTextureComponent component in pages[page.currentPage])
        {
            // the slot's name is "<itemId> <found> ..."
            if (component.containsPoint(x, y, 2))
                return ArgUtility.SplitBySpace(component.name).FirstOrDefault();
        }

        return null;
    }

    /// <summary>Say how many new collection items a geode can still give.</summary>
    private TooltipBadge? GetGeodeBadge(Item item)
    {
        if (!Context.IsWorldReady || CollectionSources.GetGeodeItems(item.QualifiedItemId) is not IReadOnlySet<string> contents)
            return null;

        int remaining = contents.Count(id => !IsFound(id));
        string text = remaining > 0
            ? this.Helper.Translation.Get("collection-hints.geode-new", new { count = remaining })
            : this.Helper.Translation.Get("collection-hints.geode-exhausted");
        return new TooltipBadge(Game1.mouseCursors, MineralIcon, Done: remaining <= 0 ? true : null, Text: text);
    }

    /// <summary>Get whether the player has found a museum item.</summary>
    private static bool IsFound(string itemId)
    {
        return Game1.player.mineralsFound.ContainsKey(itemId) || Game1.player.archaeologyFound.ContainsKey(itemId);
    }

    /// <summary>Build the tooltip for a missing museum item.</summary>
    private string GetHintText(string itemId)
    {
        if (this.Cache.TryGetValue(itemId, out string? cached))
            return cached;

        // named sources grouped by line, then vague lines for what the player hasn't met
        List<string> geodes = new(), spots = new(), panning = new(), monsters = new();
        List<string> lines = new();
        HashSet<string> vague = new();
        bool panAnywhere = false;
        int unknown = 0;

        foreach (CollectionSource source in CollectionSources.GetSources(itemId))
        {
            bool met = this.IsMet(source);
            switch (source.Kind)
            {
                case "geode":
                    if (met)
                        geodes.Add(ItemRegistry.GetDataOrErrorItem(source.Detail).DisplayName);
                    else
                        vague.Add(this.T("collection-hints.geode-unknown"));
                    break;

                case "artifactSpot":
                    if (met)
                        spots.Add(GetPlaceName(source.Detail));
                    else
                        vague.Add(this.T("collection-hints.artifact-spot-unknown"));
                    break;

                case "panning":
                    if (!met)
                        unknown++;
                    else if (GetStrings(source.Requires, "locationVisited") is { Count: > 0 } places)
                        panning.AddRange(places.Where(Game1.player.locationsVisited.Contains).Select(GetPlaceName));
                    else
                        panAnywhere = true;
                    break;

                case "monster":
                    if (met)
                        monsters.AddRange(GetStrings(source.Requires, "monsterKilled").Where(name => Game1.stats.getMonstersKilled(name) > 0).Select(this.GetMonsterName));
                    else if (GetStrings(source.Requires, "monsterKilled").Count > 0)
                        vague.Add(this.T("collection-hints.monster-unknown"));
                    else
                        unknown++;
                    break;

                case "fishingTreasure":
                    if (met)
                        lines.Add(this.T("collection-hints.fishing-treasure"));
                    else
                        unknown++;
                    break;

                case "mineNode":
                    if (met)
                        lines.Add(this.DescribeMineNode(source));
                    else if (source.Requires.TryGetProperty("mineLevel", out _) || source.Requires.TryGetProperty("skullCavern", out _) || source.Requires.TryGetProperty("volcano", out _))
                        vague.Add(this.T("collection-hints.mine-unknown"));
                    else
                        unknown++;
                    break;

                case "fishPond":
                    string? fish = GetString(source.Requires, "fishCaught") ?? GetString(source.Requires, "itemId");
                    lines.Add(met && fish != null
                        ? this.T("collection-hints.fish-pond", new { fish = ItemRegistry.GetDataOrErrorItem(fish).DisplayName })
                        : this.T("collection-hints.fish-pond-unknown"));
                    break;

                case "shop" when met && source.Detail.StartsWith("DesertFestival", StringComparison.Ordinal):
                    lines.Add(this.T("collection-hints.shop-desert-festival"));
                    break;

                case "special" when met && source.Subkind != null && OpenSpecials.Contains(source.Subkind):
                    lines.Add(this.DescribeSpecial(source));
                    break;

                default:
                    unknown++;
                    break;
            }
        }

        StringBuilder text = new(UnknownText);
        text.Append('\n').Append(this.T("collection-hints.title"));

        // plain dashes: the game's fonts don't all have a bullet
        void Add(string line) => text.Append('\n').Append("- ").Append(line);

        if (geodes.Count > 0)
            Add(this.T("collection-hints.geode", new { names = JoinNames(geodes) }));
        if (spots.Count > 0)
            Add(this.T("collection-hints.artifact-spot", new { places = JoinNames(spots) }));
        if (panAnywhere)
            Add(this.T("collection-hints.panning"));
        else if (panning.Count > 0)
            Add(this.T("collection-hints.panning-at", new { places = JoinNames(panning) }));
        if (monsters.Count > 0)
            Add(this.T("collection-hints.monster", new { names = JoinNames(monsters) }));
        foreach (string line in lines.Distinct())
            Add(line);
        foreach (string line in vague)
            Add(line);
        if (unknown > 0)
            Add(this.T("collection-hints.unknown"));

        return this.Cache[itemId] = text.ToString();
    }

    /// <summary>Say where in the mine a node can be found.</summary>
    private string DescribeMineNode(CollectionSource source)
    {
        JsonElement requires = source.Requires;

        if (requires.TryGetProperty("mineLevel", out JsonElement level) && level.TryGetInt32(out int floor))
            return floor > 120 ? this.T("collection-hints.mine-skull") : this.T("collection-hints.mine-level", new { level = floor });
        if (requires.TryGetProperty("skullCavern", out _))
            return this.T("collection-hints.mine-skull");
        if (requires.TryGetProperty("volcano", out _))
            return this.T("collection-hints.mine-volcano");
        if (requires.TryGetProperty("mailAny", out _))
            return this.T("collection-hints.mine-quarry");
        return this.T("collection-hints.mine-iridium");
    }

    /// <summary>Describe a special source which is no secret.</summary>
    private string DescribeSpecial(CollectionSource source)
    {
        string line = this.T($"collection-hints.special.{source.Subkind}");
        if (source.Subkind == "mineBarrel" && source.Requires.TryGetProperty("mineLevel", out JsonElement level) && level.TryGetInt32(out int floor))
            line += " " + this.T("collection-hints.from-floor", new { level = floor });
        return line;
    }

    /// <summary>Get whether the player has met a source, so it can be named.</summary>
    private bool IsMet(CollectionSource source)
    {
        // a few sources need nothing in the data, but say more than they should if named straight away
        if (source.Requires.ValueKind != JsonValueKind.Object || !source.Requires.EnumerateObject().Any())
        {
            return source.Subkind switch
            {
                "garbageCan" => true,
                "prizeMachine" => Game1.player.locationsVisited.Contains("ManorHouse"),
                "slimeBall" => Game1.getFarm()?.buildings.Any(building => building.buildingType.Value == "Slime Hutch") == true,
                _ => false
            };
        }

        return this.Check(source.Requires);
    }

    /// <summary>Check every requirement of a source: they must all be met.</summary>
    private bool Check(JsonElement requires)
    {
        Farmer player = Game1.player;

        foreach (JsonProperty requirement in requires.EnumerateObject())
        {
            JsonElement value = requirement.Value;
            bool met = requirement.Name switch
            {
                "geodeOpened" => value.GetString() is string geode && this.HasHadGeode(geode),
                "locationVisited" => GetStrings(value).Any(player.locationsVisited.Contains),
                "mineLevel" => value.TryGetInt32(out int level) && player.deepestMineLevel >= level,
                "skullCavern" => player.deepestMineLevel > 120 || player.locationsVisited.Contains("SkullCave"),
                "volcano" => player.locationsVisited.Contains("VolcanoDungeon0"),
                "monsterKilled" => GetStrings(value).Any(name => Game1.stats.getMonstersKilled(name) > 0),
                "fishingTreasure" => Game1.stats.Get("FishingTreasures") > 0,
                "panned" => Game1.stats.Get("TimesPanned") > 0,
                "fishCaught" => value.GetString() is string fish && player.fishCaught.ContainsKey(fish),
                "festival" => value.GetString() == "DesertFestival" && player.locationsVisited.Contains("DesertFestival"),
                "mail" => value.GetString() is string mail && player.mailReceived.Contains(mail),
                "mailAny" => GetStrings(value).Any(mail => player.mailReceived.Contains(mail) || Game1.MasterPlayer.mailReceived.Contains(mail)),
                "friendshipWith" => value.GetString() is string npc && player.friendshipData.ContainsKey(npc),
                "secretNote" => value.TryGetInt32(out int note) && player.secretNotesSeen.Contains(note),
                "itemId" => value.GetString() is string item && this.HasHadItem(item),
                "anyOf" => value.EnumerateArray().Any(this.Check),
                _ => false
            };

            if (!met)
                return false;
        }

        return true;
    }

    /// <summary>Get whether the player has had a kind of geode.</summary>
    /// <remarks>The game keeps no count per kind of geode, so what the mod saw is completed by where the player has been.</remarks>
    private bool HasHadGeode(string qualifiedId)
    {
        if (this.HasHadItem(qualifiedId))
            return true;

        return qualifiedId switch
        {
            "(O)535" => Game1.stats.GeodesCracked > 0,
            "(O)536" => Game1.player.deepestMineLevel >= 40,
            "(O)537" => Game1.player.deepestMineLevel >= 80,
            "(O)749" => Game1.player.deepestMineLevel > 20,
            "(O)MysteryBox" => Game1.player.mailReceived.Contains("sawQiPlane"),
            _ => false
        };
    }

    /// <summary>Get whether the player has had an item: carried or stored now, shipped, or received since the mod remembers it.</summary>
    private bool HasHadItem(string qualifiedId)
    {
        string? id = ItemRegistry.QualifyItemId(qualifiedId);
        if (id is null)
            return false;

        Farmer player = Game1.player;
        if (player.Items.Any(item => item?.QualifiedItemId == id) || this.Storage.Count(item => item.QualifiedItemId == id) > 0)
            return true;
        if (id.StartsWith(ItemRegistry.type_object, StringComparison.Ordinal) && player.basicShipped.ContainsKey(id[ItemRegistry.type_object.Length..]))
            return true;

        // what entered the bag since the item search started remembering it (1.5.0)
        return this.Store.Read<List<string>>(player, "items-received")?.Contains(id, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>Get a place's name as the game shows it.</summary>
    private static string GetPlaceName(string location)
    {
        return Game1.getLocationFromName(location)?.DisplayName ?? location;
    }

    /// <summary>Get a monster's name as the game shows it.</summary>
    /// <remarks>A few variants, like the haunted skull, have no name of their own in the game's data: the mod names them.</remarks>
    private string GetMonsterName(string name)
    {
        string display = Monster.GetDisplayName(name);
        if (display == name && this.Helper.Translation.Get($"collection-hints.monster-name.{name.Replace(' ', '-')}") is { } own && own.HasValue())
            return own;
        return display;
    }

    /// <summary>Join names on one line, counting the rest past a few.</summary>
    private string JoinNames(IEnumerable<string> names)
    {
        List<string> distinct = names.Distinct().ToList();
        string joined = string.Join(", ", distinct.Take(NamesPerLine));
        return distinct.Count > NamesPerLine
            ? joined + " " + this.T("collection-hints.and-more", new { count = distinct.Count - NamesPerLine })
            : joined;
    }

    private static List<string> GetStrings(JsonElement requires, string key)
    {
        return requires.ValueKind == JsonValueKind.Object && requires.TryGetProperty(key, out JsonElement value) ? GetStrings(value) : new List<string>();
    }

    private static List<string> GetStrings(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Select(entry => entry.GetString() ?? "").ToList(),
            JsonValueKind.String => new List<string> { value.GetString() ?? "" },
            _ => new List<string>()
        };
    }

    private static string? GetString(JsonElement requires, string key)
    {
        return requires.ValueKind == JsonValueKind.Object && requires.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>Forget the cached hints: a day of play changes what the player has met.</summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Cache.Clear();
    }

    /// <summary>Forget the cached hints when the menu opens, so what happened today counts.</summary>
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.NewMenu is GameMenu)
            this.Cache.Clear();
    }

    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }
}
