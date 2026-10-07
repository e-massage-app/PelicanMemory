using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;

namespace PelicanMemory.Features.MineBoards;

/// <summary>The player's records in the Skull Cavern, kept by the mod since the game keeps none.</summary>
/// <param name="BestDayFloor">The deepest floor reached in a single day.</param>
/// <param name="BestDayIridium">The most iridium ore obtained in the cavern in a single day.</param>
/// <param name="DeepestIridiumFloor">The deepest floor where iridium ore was obtained.</param>
/// <param name="Since">When the mod started keeping them, as the game writes dates.</param>
internal record CavernRecords(int BestDayFloor, int BestDayIridium, int DeepestIridiumFloor, string Since);

/// <summary>Two boards: by the mines' elevator, what each zone holds; at the Skull Cavern's entrance, depth, iridium and records.</summary>
/// <remarks>
/// Memory, not a guide: a zone is only described once the player has reached it, from the game's own code (which ores
/// mainly, which gems and geodes, which monsters); deeper zones stay "????". The game counts lifetime totals (copper,
/// iron, gold, iridium found...) but nothing per floor or per trip, so the cavern records start when the mod is
/// installed. The boards are drawn by the mod, not placed in the world: nothing is added to the save but the records.
/// </remarks>
internal class MineBoardsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static MineBoardsFeature? Instance;

    /// <summary>The key of the cavern records in the player's own save data.</summary>
    private const string RecordsKey = "cavern-records";

    /// <summary>The game's first Skull Cavern level: its floor 1 is mine level 121.</summary>
    private const int CavernStart = 120;

    /// <summary>The wall tiles the notes are pinned on, checked on the game's maps: left of the elevator's switch, right of the cavern's door, each with walkable floor below to read it from.</summary>
    private static readonly Dictionary<string, Vector2> BoardTiles = new()
    {
        ["Mine"] = new Vector2(15, 3),
        ["SkullCave"] = new Vector2(5, 3)
    };

    /// <summary>The sheet of paper drawn pinned to the wall, so it looks like part of the place rather than something added.</summary>
    private const string NoteItemId = "(O)842";

    /// <summary>What each mine zone holds, read from the game's code (1.6.15).</summary>
    private static readonly MineZone[] Zones =
    {
        new(1, 39,
            Ores: new[] { ("(O)378", "mainly"), ("(O)380", "rare") },
            Gems: new[] { ("(O)66", 2), ("(O)68", 2), ("(O)70", 2), ("(O)62", 2), ("(O)64", 2), ("(O)60", 2) },
            Geodes: new[] { ("(O)535", 1), ("(O)749", 21) },
            Monsters: new[] { ("Green Slime", 1), ("Bug", 1), ("Duggy", 1), ("Rock Crab", 1), ("Blue Squid", 11), ("Grub", 15), ("Fly", 15), ("Bat", 31), ("Stone Golem", 31) }),
        new(40, 79,
            Ores: new[] { ("(O)380", "mainly"), ("(O)378", "sometimes"), ("(O)384", "rare") },
            Gems: new[] { ("(O)66", 40), ("(O)68", 40), ("(O)70", 40), ("(O)62", 40), ("(O)64", 40), ("(O)60", 40), ("(O)72", 51) },
            Geodes: new[] { ("(O)536", 40), ("(O)749", 40) },
            Monsters: new[] { ("Dust Spirit", 40), ("Frost Bat", 40), ("Frost Jelly", 40), ("Spider", 50), ("Ghost", 51), ("Skeleton", 70) }),
        new(80, 119,
            Ores: new[] { ("(O)384", "mainly"), ("(O)380", "sometimes"), ("(O)378", "rare") },
            Gems: new[] { ("(O)66", 80), ("(O)68", 80), ("(O)70", 80), ("(O)62", 80), ("(O)64", 80), ("(O)60", 80), ("(O)72", 80) },
            Geodes: new[] { ("(O)537", 80), ("(O)749", 80) },
            Monsters: new[] { ("Lava Bat", 80), ("Sludge", 80), ("Metal Head", 80), ("Shadow Brute", 80), ("Shadow Shaman", 80), ("Lava Crab", 80), ("Squid Kid", 90) },
            MysticFrom: 101)
    };

    /// <summary>The Skull Cavern's monsters, with the cavern floor they appear from.</summary>
    private static readonly (string Name, int From)[] CavernMonsters =
    {
        ("Serpent", 1), ("Bug", 1), ("Sludge", 1), ("Big Slime", 1), ("Mummy", 1), ("Carbon Ghost", 1), ("Pepper Rex", 6), ("Lava Bat", 20), ("Iridium Crab", 26), ("Iridium Bat", 51)
    };

    private readonly PlayerStore Store;

    /// <summary>Today's deepest cavern floor and iridium obtained in the cavern.</summary>
    private int TodayFloor;
    private int TodayIridium;

    /// <summary>The game's lifetime iridium count when last read, to see how much was just obtained.</summary>
    private uint LastIridiumFound;

    private record MineZone(int From, int To, (string Item, string Frequency)[] Ores, (string Item, int From)[] Gems, (string Item, int From)[] Geodes, (string Name, int From)[] Monsters, int MysticFrom = 0);


    /*********
    ** Public methods
    *********/
    public override string Id => "mine-boards";

    public MineBoardsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store)
        : base(helper, monitor, harmony, settings)
    {
        this.Store = store;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Postfix(AccessTools.Method(typeof(GameLocation), nameof(GameLocation.draw), new[] { typeof(SpriteBatch) }), typeof(MineBoardsFeature), nameof(After_LocationDraw));
        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        this.Helper.Events.Player.Warped += this.OnWarped;
        this.Helper.Events.GameLoop.SaveLoaded += this.OnDayBegins;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayBegins;
        this.Helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    protected override void OnDisable()
    {
        Instance = null;
        this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed;
        this.Helper.Events.Player.Warped -= this.OnWarped;
        this.Helper.Events.GameLoop.SaveLoaded -= this.OnDayBegins;
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayBegins;
        this.Helper.Events.GameLoop.UpdateTicked -= this.OnUpdateTicked;
    }


    /*********
    ** Private methods: the boards
    *********/
    /// <summary>Draw the note with the place itself, so the player standing in front of it is drawn over it, as with the game's own wall decor.</summary>
    private static void After_LocationDraw(GameLocation __instance, SpriteBatch b)
    {
        if (Instance is null || !BoardTiles.TryGetValue(__instance.Name, out Vector2 tile))
            return;

        // a sheet pinned on the wall at eye height, just above the wall tile's lower edge
        ParsedItemData note = ItemRegistry.GetDataOrErrorItem(NoteItemId);
        Rectangle source = note.GetSourceRect();
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, new Vector2(tile.X * 64 + 8, tile.Y * 64 + 4));
        b.Draw(note.GetTexture(), position, source, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, Math.Max(0f, (tile.Y * 64 + 65) / 10000f));
    }

    /// <summary>Read the board when the player uses it, like any sign.</summary>
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsPlayerFree || !e.Button.IsActionButton() || Game1.currentLocation is not GameLocation location || !BoardTiles.TryGetValue(location.Name, out Vector2 tile))
            return;

        // the note's wall tile, aimed at from the floor below it
        bool aimed = e.Cursor.GrabTile == tile || e.Cursor.Tile == tile;
        if (!aimed || Vector2.Distance(Game1.player.Tile, tile) > 2.5f)
            return;

        this.Helper.Input.Suppress(e.Button);
        try
        {
            string text = location.Name == "Mine" ? this.BuildMinesText() : this.BuildCavernText();
            Game1.activeClickableMenu = new LetterViewerMenu(text);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to read a mine board:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Write the mines board: each zone reached, then the lifetime totals.</summary>
    private string BuildMinesText()
    {
        Farmer player = Game1.player;
        int deepest = Math.Min(player.deepestMineLevel, CavernStart);
        StringBuilder text = new();
        text.Append(this.T("mine-boards.mines-title")).Append("^^");

        foreach (MineZone zone in Zones)
        {
            text.Append(this.T("mine-boards.zone", new { from = zone.From, to = zone.To })).Append('^');
            if (deepest < zone.From)
            {
                text.Append("  ????^^");
                continue;
            }

            // the game hides most gems from the first zone until the bottom of the mines has been reached once
            IEnumerable<(string Item, int From)> gems = zone.Gems;
            if (zone.From == 1 && player.timesReachedMineBottom <= 0)
                gems = gems.Where(gem => gem.Item is "(O)66" or "(O)68");

            text.Append("  ").Append(this.T("mine-boards.ores", new { list = string.Join(", ", zone.Ores.Select(ore => $"{Name(ore.Item)} ({this.T($"mine-boards.frequency.{ore.Frequency}")})")) })).Append('^');
            text.Append("  ").Append(this.T("mine-boards.gems", new { list = this.ListFrom(gems, zone.From, deepest) })).Append('^');
            text.Append("  ").Append(this.T("mine-boards.geodes", new { list = this.ListFrom(zone.Geodes, zone.From, deepest) })).Append('^');
            if (zone.MysticFrom > 0 && deepest >= zone.MysticFrom)
                text.Append("  ").Append(this.T("mine-boards.mystic", new { level = zone.MysticFrom })).Append('^');
            text.Append("  ").Append(this.T("mine-boards.monsters", new { list = string.Join(", ", zone.Monsters.Where(monster => monster.From <= deepest).Select(monster => Monster.GetDisplayName(monster.Name)).Distinct()) })).Append("^^");
        }

        text.Append(this.BuildTotalsText());
        return text.ToString();
    }

    /// <summary>Write the cavern board: record depth, iridium by depth up to it, the monsters met, and the records.</summary>
    private string BuildCavernText()
    {
        Farmer player = Game1.player;
        int record = Math.Max(0, player.deepestMineLevel - CavernStart);
        StringBuilder text = new();
        text.Append(this.T("mine-boards.cavern-title")).Append("^^");
        text.Append(this.T("mine-boards.cavern-record", new { floor = record })).Append("^^");

        if (record > 0)
        {
            // how rare iridium is at each depth reached, from the game's own formula; deeper stays a promise, not a number
            text.Append(this.T("mine-boards.iridium-title")).Append('^');
            foreach (int floor in GetDepthSteps(record))
                text.Append("  ").Append(this.T("mine-boards.iridium-row", new { floor, odds = GetIridiumOdds(floor) })).Append('^');
            text.Append("  ").Append(this.T("mine-boards.iridium-deeper")).Append("^^");

            string monsters = string.Join(", ", CavernMonsters.Where(monster => monster.From <= record).Select(monster => Monster.GetDisplayName(monster.Name)).Distinct());
            text.Append(this.T("mine-boards.monsters", new { list = monsters })).Append("^^");
        }

        CavernRecords records = this.ReadRecords();
        text.Append(this.T("mine-boards.records-title", new { date = records.Since })).Append('^');
        text.Append("  ").Append(this.T("mine-boards.record-day-floor", new { floor = records.BestDayFloor })).Append('^');
        text.Append("  ").Append(this.T("mine-boards.record-day-iridium", new { count = records.BestDayIridium })).Append('^');
        text.Append("  ").Append(this.T("mine-boards.record-iridium-floor", new { floor = records.DeepestIridiumFloor })).Append('^');
        text.Append("  ").Append(this.T("mine-boards.today", new { floor = this.TodayFloor, count = this.TodayIridium })).Append("^^");

        text.Append(this.BuildTotalsText());
        return text.ToString();
    }

    /// <summary>Write the lifetime totals the game counts.</summary>
    private string BuildTotalsText()
    {
        Stats stats = Game1.player.stats;
        StringBuilder text = new();
        text.Append(this.T("mine-boards.totals-title")).Append('^');
        text.Append("  ").Append(string.Join(", ", new[]
        {
            $"{Name("(O)378")} {stats.CopperFound}",
            $"{Name("(O)380")} {stats.IronFound}",
            $"{Name("(O)384")} {stats.GoldFound}",
            $"{Name("(O)386")} {stats.IridiumFound}"
        })).Append('^');
        text.Append("  ").Append(this.T("mine-boards.totals-other", new { diamonds = stats.DiamondsFound, rocks = stats.RocksCrushed, geodes = stats.GeodesCracked })).Append('^');
        return text.ToString();
    }

    /// <summary>List items, noting those which only appear from a floor deeper than the zone's start.</summary>
    /// <remarks>Grouped by floor, so the floor is said once: "Geode; then from floor 21: Omni Geode".</remarks>
    private string ListFrom(IEnumerable<(string Item, int From)> items, int zoneStart, int deepest)
    {
        // a floor or two into the zone is the zone itself: no need to say it
        IEnumerable<IGrouping<int, (string Item, int From)>> groups = items
            .Where(item => item.From <= deepest)
            .GroupBy(item => item.From <= zoneStart + 1 ? zoneStart : item.From)
            .OrderBy(group => group.Key);

        List<string> parts = new();
        foreach (IGrouping<int, (string Item, int From)> group in groups)
        {
            string names = string.Join(", ", group.Select(item => Name(item.Item)));
            parts.Add(group.Key == zoneStart ? names : this.T("mine-boards.then-from", new { level = group.Key, list = names }));
        }
        return string.Join(" ; ", parts);
    }

    /// <summary>Get the depths to describe: every ten floors up to the record, and the record itself.</summary>
    private static IEnumerable<int> GetDepthSteps(int record)
    {
        List<int> steps = new();
        for (int floor = 10; floor <= Math.Min(record, 100); floor += 10)
            steps.Add(floor);
        if (record < 10 || (record <= 100 && record % 10 != 0))
            steps.Add(Math.Max(1, record));
        return steps;
    }

    /// <summary>Get how many stones it takes, on average, to meet an iridium node at a cavern depth.</summary>
    /// <remarks>
    /// The game's formula (MineShaft.chooseStoneType, 1.6.15): a stone becomes an ore node with a chance growing with
    /// depth, and that node is iridium with another chance growing with depth (up to floor 100). Luck and special floors
    /// aside, so it's "about".
    /// </remarks>
    public static int GetIridiumOdds(int floor)
    {
        double oreChance = 0.02 + floor * 0.0005;
        if (floor >= 10)
            oreChance += 0.01 * ((Math.Min(100, floor) - 10) / 10f);

        double bonus = floor >= 10 ? Math.Min(0.004, 0.001 * ((floor - 10) / 10f)) : 0;
        if (floor > 100)
            bonus += floor / 1000000.0;

        double iridiumShare = Math.Min(100, floor) * (0.0003 + bonus);
        double perStone = oreChance * iridiumShare;
        return perStone > 0 ? (int)Math.Round(1 / perStone) : int.MaxValue;
    }

    private static string Name(string itemId)
    {
        return ItemRegistry.GetDataOrErrorItem(itemId).DisplayName;
    }


    /*********
    ** Private methods: the records
    *********/
    /// <summary>Start a new day of records.</summary>
    private void OnDayBegins(object? sender, EventArgs e)
    {
        this.TodayFloor = 0;
        this.TodayIridium = 0;
        this.LastIridiumFound = Game1.player.stats.IridiumFound;
    }

    /// <summary>Note how deep the player went in the cavern today.</summary>
    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer || e.NewLocation is not MineShaft shaft || shaft.mineLevel <= CavernStart)
            return;

        int floor = shaft.mineLevel - CavernStart;
        if (floor <= this.TodayFloor)
            return;

        this.TodayFloor = floor;
        CavernRecords records = this.ReadRecords();
        if (floor > records.BestDayFloor)
            this.WriteRecords(records with { BestDayFloor = floor });
    }

    /// <summary>Count the iridium ore obtained in the cavern, from the game's own lifetime count.</summary>
    /// <remarks>Read from the game's count rather than the bag, so ore dropped and picked up again, or moved through a chest, isn't counted twice.</remarks>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !e.IsMultipleOf(15))
            return;

        uint found = Game1.player.stats.IridiumFound;
        if (found <= this.LastIridiumFound)
        {
            this.LastIridiumFound = found;
            return;
        }

        int gained = (int)(found - this.LastIridiumFound);
        this.LastIridiumFound = found;

        if (Game1.currentLocation is not MineShaft shaft || shaft.mineLevel <= CavernStart)
            return;

        this.TodayIridium += gained;
        int floor = shaft.mineLevel - CavernStart;

        CavernRecords records = this.ReadRecords();
        CavernRecords updated = records with
        {
            BestDayIridium = Math.Max(records.BestDayIridium, this.TodayIridium),
            DeepestIridiumFloor = Math.Max(records.DeepestIridiumFloor, floor)
        };
        if (updated != records)
            this.WriteRecords(updated);
    }

    /// <summary>Read the player's cavern records, starting them today if there are none yet.</summary>
    private CavernRecords ReadRecords()
    {
        return this.Store.Read<CavernRecords>(Game1.player, RecordsKey) ?? new CavernRecords(0, 0, 0, Game1.Date.Localize());
    }

    private void WriteRecords(CavernRecords records)
    {
        this.Store.Write(Game1.player, RecordsKey, records);
    }

    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }
}
