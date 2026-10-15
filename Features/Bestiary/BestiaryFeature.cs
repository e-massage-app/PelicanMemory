using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.TokenizableStrings;

namespace PelicanMemory.Features.Bestiary;

/// <summary>A "Monsters" tab in the collections: every monster's face, how many you've beaten, Gil's goals, and where to find them.</summary>
/// <remarks>
/// The game counts every monster beaten but only shows it on a sheet pinned in the Adventurer's Guild, and Gil only
/// speaks once a goal is reached. The tab shows it like the fish: a silhouette and "????" until beaten, then the face,
/// with the count, what's left for Gil, and the places it lives. A family whose goal is done is darkened and ticked.
/// A button switches between one icon per monster and one per Gil family.
///
/// Anti-spoil: a monster never beaten stays a silhouette with nothing said; a place is only named once visited (the
/// mines up to the deepest floor reached); a reward is named once collected.
///
/// The collections side tabs are moved 8 px closer to each other so a ninth fits in the frame (approved by Jordan on a mock-up).
/// </remarks>
internal class BestiaryFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static BestiaryFeature? Instance;

    /// <summary>The tab's number in the collections page (the game's go up to 7).</summary>
    public const int TabId = 8;

    /// <summary>The setting remembering the view.</summary>
    private const string ByFamilyFlag = "bestiary.by-family";

    /// <summary>The distance between two side tabs, 64 in the game: 8 less lets nine fit where eight did.</summary>
    private const int TabSpacing = 56;

    private const int Columns = 10;
    private const int Step = 68;

    /// <summary>The skull on the game's cursor sheet, drawn on the tab.</summary>
    private static readonly Rectangle SkullIcon = new(140, 428, 10, 10);

    /// <summary>A side tab of the game (the achievements star), emptied to frame the skull.</summary>
    private static readonly Rectangle TabSource = new(656, 80, 16, 16);

    private static readonly AccessTools.FieldRef<CollectionsPage, string> HoverText = AccessTools.FieldRefAccess<CollectionsPage, string>("hoverText");
    private static readonly AccessTools.FieldRef<CollectionsPage, Item?> HoverItem = AccessTools.FieldRefAccess<CollectionsPage, Item?>("hoverItem");

    /// <summary>The tab's texture: the game's tab with the skull, built once.</summary>
    private Texture2D? TabTexture;

    /// <summary>The tooltip of each icon, worked out once per opening of the tab: the game asks on every frame.</summary>
    private readonly Dictionary<string, string> Descriptions = new();


    /*********
    ** Public methods
    *********/
    public override string Id => "bestiary";

    public BestiaryFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }

    /// <summary>A goal of Gil's, as the player stands.</summary>
    internal record Goal(string Key, string Name, string[] Targets, int Killed, int Target, bool Collected, string? RewardItemId)
    {
        public bool IsDone => this.Killed >= this.Target;
    }

    /// <summary>Read Gil's goals for the local player, as the game counts them (<c>AdventureGuild.showMonsterKillList</c>).</summary>
    internal static List<Goal> ReadGoals()
    {
        List<Goal> goals = new();
        foreach ((string key, MonsterSlayerQuestData data) in DataLoader.MonsterSlayerQuests(Game1.content))
        {
            string[] targets = data.Targets?.ToArray() ?? Array.Empty<string>();
            int killed = targets.Sum(target => Game1.stats.getMonstersKilled(target));
            goals.Add(new Goal(key, TokenParser.ParseText(data.DisplayName), targets, killed, data.Count, AdventureGuild.HasCollectedReward(Game1.player, key), data.RewardItemId));
        }
        return goals;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Postfix(
            AccessTools.Constructor(typeof(CollectionsPage), new[] { typeof(int), typeof(int), typeof(int), typeof(int) }),
            typeof(BestiaryFeature),
            nameof(After_Constructor)
        );
        this.Prefix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.performHoverAction)),
            typeof(BestiaryFeature),
            nameof(Before_PerformHoverAction)
        );
        this.Postfix(
            AccessTools.Method(typeof(CollectionsPage), nameof(CollectionsPage.receiveLeftClick)),
            typeof(BestiaryFeature),
            nameof(After_ReceiveLeftClick)
        );
    }

    protected override void OnDisable()
    {
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Add the tab and its icons, and close up the side tabs so it fits.</summary>
    private static void After_Constructor(CollectionsPage __instance)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            Instance.AddTab(__instance);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to add the monsters tab:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Describe the hovered monster; the game's own description only knows items.</summary>
    private static bool Before_PerformHoverAction(CollectionsPage __instance, int x, int y)
    {
        if (Instance is null || __instance.currentTab != TabId || __instance.letterviewerSubMenu != null)
            return true;

        try
        {
            Instance.Hover(__instance, x, y);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to describe a monster:\n{ex}", LogLevel.Error);
        }
        return false;
    }

    /// <summary>Switch between monsters and families.</summary>
    private static void After_ReceiveLeftClick(CollectionsPage __instance, int x, int y)
    {
        if (Instance is null || __instance.currentTab != TabId || !__instance.collections.TryGetValue(TabId, out var pages) || pages.Count == 0)
            return;

        try
        {
            if (pages[Math.Min(__instance.currentPage, pages.Count - 1)].OfType<BestiaryViewToggle>().FirstOrDefault() is { } toggle && toggle.containsPoint(x, y))
            {
                Instance.Settings.SetFlag(ByFamilyFlag, !Instance.Settings.GetFlag(ByFamilyFlag, false));
                Instance.BuildIcons(__instance);
                __instance.currentPage = 0;
                Game1.playSound("smallSelect");
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to switch the monsters view:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }

    /// <summary>Add the side tab, move the side tabs closer together, and fill the tab.</summary>
    private void AddTab(CollectionsPage page)
    {
        page.sideTabs[TabId] = new ClickableTextureComponent(TabId.ToString(), new Rectangle(page.xPositionOnScreen - 48, 0, 64, 64), "", this.T("bestiary.tab"), this.GetTabTexture(), new Rectangle(0, 0, 16, 16), 4f)
        {
            myID = 7009,
            upNeighborID = -99998,
            rightNeighborID = 0
        };

        // the game's order, top to bottom, closer together; ours last
        List<ClickableTextureComponent> tabs = page.sideTabs
            .OrderBy(pair => pair.Key == TabId ? int.MaxValue : pair.Value.bounds.Y)
            .Select(pair => pair.Value)
            .ToList();
        for (int i = 0; i < tabs.Count; i++)
        {
            tabs[i].bounds.Y = page.yPositionOnScreen + 64 * 2 + i * TabSpacing;
            tabs[i].downNeighborID = i == tabs.Count - 1 ? -1 : -99998;
            tabs[i].downNeighborImmutable = i == tabs.Count - 1;
        }

        this.BuildIcons(page);
    }

    /// <summary>Get the tab's texture: the game's achievements tab, emptied, with the skull from the same sheet.</summary>
    private Texture2D GetTabTexture()
    {
        if (this.TabTexture is { IsDisposed: false })
            return this.TabTexture;

        Color[] cursors = new Color[Game1.mouseCursors.Width * Game1.mouseCursors.Height];
        Game1.mouseCursors.GetData(cursors);
        Color At(int x, int y) => cursors[y * Game1.mouseCursors.Width + x];

        Color[] tab = new Color[16 * 16];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                tab[y * 16 + x] = At(TabSource.X + x, TabSource.Y + y);

        // the tab's inside, as on the game's tabs: a light edge and a lighter middle
        Color edge = At(TabSource.X + 3, TabSource.Y + 3);
        Color middle = At(TabSource.X + 5, TabSource.Y + 4);
        for (int y = 3; y <= 12; y++)
        {
            for (int x = 3; x < 16; x++)
            {
                if (y is 3 or 12)
                {
                    if (x >= 4)
                        tab[y * 16 + x] = edge;
                }
                else
                    tab[y * 16 + x] = x is 3 or 15 ? edge : middle;
            }
        }

        // the skull, centred
        for (int y = 0; y < SkullIcon.Height; y++)
        {
            for (int x = 0; x < SkullIcon.Width; x++)
            {
                Color pixel = At(SkullIcon.X + x, SkullIcon.Y + y);
                if (pixel.A > 0)
                    tab[(3 + y) * 16 + 5 + x] = pixel;
            }
        }

        this.TabTexture = new Texture2D(Game1.graphics.GraphicsDevice, 16, 16);
        this.TabTexture.SetData(tab);
        return this.TabTexture;
    }

    /// <summary>Lay out the icons of the current view, in pages like the game's tabs, with the view button under them.</summary>
    private void BuildIcons(CollectionsPage page)
    {
        bool byFamily = this.Settings.GetFlag(ByFamilyFlag, false);
        List<Goal> goals = ReadGoals();
        this.Descriptions.Clear();
        Dictionary<string, MonsterEntry> monsters = BestiaryData.GetMonsters().ToDictionary(monster => monster.Key);

        List<(string Key, bool Beaten, bool Done, MonsterEntry Face)> entries = new();
        if (byFamily)
        {
            foreach (Goal goal in goals)
            {
                // the family's face: its first monster beaten, else its first monster
                MonsterEntry? face = goal.Targets.Where(monsters.ContainsKey).Select(target => monsters[target]).OrderByDescending(monster => Game1.stats.getMonstersKilled(monster.Key) > 0).FirstOrDefault();
                if (face != null)
                    entries.Add((goal.Key, goal.Killed > 0, goal.IsDone, face));
            }
        }
        else
        {
            foreach (MonsterEntry monster in monsters.Values)
            {
                Goal? goal = goals.FirstOrDefault(goal => goal.Targets.Contains(monster.Key));
                entries.Add((monster.Key, Game1.stats.getMonstersKilled(monster.Key) > 0, goal?.IsDone == true, monster));
            }
        }

        int left = page.xPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearSideBorder;
        int top = page.yPositionOnScreen + IClickableMenu.borderWidth + IClickableMenu.spaceToClearTopBorder - 16;
        int bottom = page.yPositionOnScreen + page.height - 128 - Step; // a row kept free for the view button

        List<List<ClickableTextureComponent>> pages = new() { new List<ClickableTextureComponent>() };
        int index = 0;
        foreach (var (key, beaten, done, face) in entries)
        {
            int x = left + index % Columns * Step;
            int y = top + index / Columns * Step;
            if (y > bottom)
            {
                pages.Add(new List<ClickableTextureComponent>());
                index = 0;
                x = left;
                y = top;
            }

            Rectangle source = new(face.Icon[0], face.Icon[1], face.Icon[2], face.Icon[3]);
            Color tint = face.Tint is { Length: 3 } t ? new Color(t[0], t[1], t[2]) : Color.White;
            List<ClickableTextureComponent> list = pages.Last();
            list.Add(new MonsterIcon(key, byFamily, beaten, done, new Rectangle(x, y, 64, 64), Game1.content.Load<Texture2D>(face.Texture), source, tint)
            {
                myID = list.Count,
                rightNeighborID = (list.Count + 1) % Columns == 0 ? -1 : list.Count + 1,
                leftNeighborID = list.Count % Columns == 0 ? 7009 : list.Count - 1,
                downNeighborID = list.Count + Columns,
                upNeighborID = list.Count < Columns ? 12347 : list.Count - Columns,
                fullyImmutable = true
            });
            index++;
        }

        // the view button, centred on the arrows' row
        string label = this.T(byFamily ? "bestiary.view-family" : "bestiary.view-monster");
        int width = (int)Game1.smallFont.MeasureString(label).X + 48;
        Rectangle buttonArea = new(page.xPositionOnScreen + page.width / 2 - width / 2, page.yPositionOnScreen + page.height - 88, width, 56);
        foreach (List<ClickableTextureComponent> list in pages)
            list.Add(new BestiaryViewToggle(buttonArea, label) { myID = 7100 });

        page.collections[TabId] = pages;
    }

    /// <summary>Set the tooltip for what's under the cursor, as the game does for its own tabs.</summary>
    private void Hover(CollectionsPage page, int x, int y)
    {
        HoverText(page) = "";
        HoverItem(page) = null;
        page.secretNoteImage = -1;

        foreach (ClickableTextureComponent tab in page.sideTabs.Values)
        {
            if (tab.containsPoint(x, y))
            {
                HoverText(page) = tab.hoverText;
                return;
            }
        }

        List<ClickableTextureComponent> icons = page.collections[TabId][Math.Min(page.currentPage, page.collections[TabId].Count - 1)];
        foreach (ClickableTextureComponent component in icons)
        {
            if (component is BestiaryViewToggle toggle)
            {
                toggle.scale = toggle.containsPoint(x, y) ? toggle.baseScale + 0.1f : toggle.baseScale;
                if (toggle.containsPoint(x, y))
                    HoverText(page) = this.T("bestiary.view-hint");
                continue;
            }

            if (component is not MonsterIcon icon)
                continue;

            if (icon.containsPoint(x, y, 2))
            {
                icon.scale = Math.Min(icon.scale + 0.02f, icon.baseScale + 0.1f);
                string cacheKey = (icon.IsFamily ? "family:" : "") + icon.Key;
                if (!this.Descriptions.TryGetValue(cacheKey, out string? description))
                    this.Descriptions[cacheKey] = description = icon.IsFamily ? this.DescribeFamily(icon.Key) : this.DescribeMonster(icon.Key);
                HoverText(page) = description;
            }
            else
                icon.scale = Math.Max(icon.scale - 0.02f, icon.baseScale);
        }

        page.forwardButton.tryHover(x, y, 0.5f);
        page.backButton.tryHover(x, y, 0.5f);
    }

    /// <summary>Describe one monster: its count, Gil's goal for its family, and where it lives.</summary>
    private string DescribeMonster(string key)
    {
        int killed = Game1.stats.getMonstersKilled(key);
        if (killed <= 0)
            return "????";

        List<string> lines = new() { this.GetMonsterName(key), "", this.T("bestiary.killed", new { count = killed }) };

        if (ReadGoals().FirstOrDefault(goal => goal.Targets.Contains(key)) is { } goal)
            lines.Add(this.DescribeGoal(goal));

        MonsterEntry? entry = BestiaryData.GetMonsters().FirstOrDefault(monster => monster.Key == key);
        if (entry != null)
            lines.AddRange(this.DescribePlaces(entry));

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Describe one of Gil's families: the goal, and its monsters (only those beaten are named).</summary>
    private string DescribeFamily(string goalKey)
    {
        Goal? goal = ReadGoals().FirstOrDefault(goal => goal.Key == goalKey);
        if (goal is null || goal.Killed <= 0)
            return "????";

        List<string> lines = new() { goal.Name, "", this.DescribeGoal(goal), "" };
        foreach (string target in goal.Targets.Where(target => BestiaryData.GetMonsters().Any(monster => monster.Key == target)))
        {
            int killed = Game1.stats.getMonstersKilled(target);
            lines.Add(killed > 0 ? this.T("bestiary.member", new { name = this.GetMonsterName(target), count = killed }) : this.T("bestiary.member-unknown"));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Describe where a goal stands.</summary>
    private string DescribeGoal(Goal goal)
    {
        if (!goal.IsDone)
            return this.T("bestiary.goal", new { killed = goal.Killed, target = goal.Target, left = goal.Target - goal.Killed });
        if (!goal.Collected)
            return this.T("bestiary.goal-to-collect");
        return goal.RewardItemId != null
            ? this.T("bestiary.goal-done-reward", new { item = ItemRegistry.GetDataOrErrorItem(goal.RewardItemId).DisplayName })
            : this.T("bestiary.goal-done");
    }

    /// <summary>List the places a monster lives that the player knows, the best one marked; the others only counted.</summary>
    private IEnumerable<string> DescribePlaces(MonsterEntry entry)
    {
        List<MonsterPlace> known = entry.Places.Where(IsKnown).ToList();
        int unknown = entry.Places.Length - known.Count;
        if (known.Count == 0 && unknown == 0)
            yield break;

        yield return "";
        yield return this.T("bestiary.where");
        foreach (MonsterPlace place in known.OrderByDescending(place => place.Best))
        {
            string text = "  " + this.GetPlaceText(place);
            if (place.Note != null)
                text += $" ({this.T($"bestiary.note.{place.Note}")})";
            if (place.Best && entry.Places.Length > 1)
                text += " — " + this.T("bestiary.best");
            yield return text;
        }
        if (unknown > 0)
            yield return "  " + this.T("bestiary.places-unknown", new { count = unknown });
    }

    /// <summary>Get whether the player knows a place: visited, or reached in the mines.</summary>
    private static bool IsKnown(MonsterPlace place)
    {
        Farmer player = Game1.player;

        // the dangerous mines are only named once the player has fought in them
        if (place.Note == "dangerous" && Game1.stats.Get("hardModeMonstersKilled") == 0)
            return false;

        return place.Kind switch
        {
            "mine" => player.deepestMineLevel >= place.From,
            "cavern" => player.deepestMineLevel >= 120 + Math.Max(1, place.From),
            "volcano" => GeneratedLevels.HasBeenInVolcano(player),
            "quarry" => Game1.stats.getMonstersKilled("Haunted Skull") > 0,
            "farm" => Game1.spawnMonstersAtNight,
            "location" => place.Names?.Any(player.locationsVisited.Contains) == true,
            _ => false
        };
    }

    /// <summary>Get how a place is written.</summary>
    private string GetPlaceText(MonsterPlace place)
    {
        return place.Kind switch
        {
            "mine" => this.T("bestiary.place.mine", new { from = place.From, to = Math.Min(place.To, Game1.player.deepestMineLevel) }),
            "cavern" => place.From > 1 ? this.T("bestiary.place.cavern-from", new { from = place.From }) : this.T("bestiary.place.cavern"),
            "volcano" => this.T("bestiary.place.volcano"),
            "quarry" => this.T("bestiary.place.quarry"),
            "farm" => this.T("bestiary.place.farm"),
            "location" => string.Join(", ", place.Names!.Where(Game1.player.locationsVisited.Contains).Select(name => Game1.getLocationFromName(name)?.DisplayName ?? name).Distinct()),
            _ => place.Kind
        };
    }

    /// <summary>Get a monster's name as the game shows it, or the mod's own for the few variants the game doesn't name.</summary>
    private string GetMonsterName(string key)
    {
        string display = Monster.GetDisplayName(key);
        if (display == key && this.Helper.Translation.Get($"bestiary.name.{key.Replace(' ', '-')}") is { } own && own.HasValue())
            return own;
        return display;
    }
}
