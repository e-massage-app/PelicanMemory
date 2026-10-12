using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;
using StardewValley.SpecialOrders;

namespace PelicanMemory.Features.FairDeadlines;

/// <summary>Fairer deadlines for quests and special orders: a day you couldn't use doesn't count.</summary>
/// <remarks>
/// Each night the game takes one day off every quest with a deadline, including the day it was taken (even at 7pm) and
/// festival days, when the village is at the fair and the request can't be done. Here, just before the game counts the
/// day that ends, that day is given back to:
/// <list type="bullet">
///   <item>the quests taken that day after noon (the hour can be changed in config.json);</item>
///   <item>every quest under way, when the day was a festival: the village's (egg hunt, flower dance, luau…) and the
///         ones that last several days (night market, desert festival, trout derby, SquidFest). Birthdays don't count.</item>
/// </list>
/// So the counter simply doesn't move for those days. The special orders' deadline is shared, so the host moves it.
/// </remarks>
internal class FairDeadlinesFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The config key: from what time a quest taken that day doesn't lose that day (default noon).</summary>
    private const string CutoffKey = "fair-deadlines.cutoff";

    /// <summary>The config key: the festivals which last several days pause the counter too.</summary>
    private const string PassiveKey = "fair-deadlines.passive-festivals";

    /// <summary>The quests and orders already seen today (so a new one can be told apart, with the time it was taken).</summary>
    private readonly HashSet<object> Known = new(ReferenceEqualityComparer.Instance);

    /// <summary>The quests and orders taken today after the cutoff.</summary>
    private readonly HashSet<object> TakenLate = new(ReferenceEqualityComparer.Instance);

    /// <summary>What to tell the player in the morning, if the night gave days back.</summary>
    private string? MorningNote;


    /*********
    ** Public methods
    *********/
    public override string Id => "fair-deadlines";

    /// <summary>The active instance, for the automated tests.</summary>
    internal static FairDeadlinesFeature? Active { get; private set; }

    public FairDeadlinesFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }

    /// <inheritdoc />
    public override IEnumerable<OptionsElement> CreateOptionRows()
    {
        yield return new FeatureCheckbox(
            label: this.Helper.Translation.Get("option.fair-deadlines-passive"),
            description: this.Helper.Translation.Get("option.fair-deadlines-passive.description"),
            isChecked: this.Settings.GetFlag(PassiveKey, true),
            onToggled: value => this.Settings.SetFlag(PassiveKey, value)
        );
    }

    /// <summary>Whether today is a day the counter shouldn't move: a village festival, or a festival lasting several days.</summary>
    public bool IsPauseDay()
    {
        if (Utility.isFestivalDay(Game1.dayOfMonth, Game1.season))
            return true;
        return this.Settings.GetFlag(PassiveKey, true) && Game1.netWorldState.Value.ActivePassiveFestivals.Count > 0;
    }

    /// <summary>For tests: the quests and orders this day will be given back to tonight.</summary>
    public IEnumerable<object> WillGiveBack()
    {
        bool pause = this.IsPauseDay();
        foreach (Quest quest in TimedQuests())
            if (pause || this.TakenLate.Contains(quest))
                yield return quest;
        foreach (SpecialOrder order in RunningOrders())
            if (pause || this.TakenLate.Contains(order))
                yield return order;
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Active = this;
        this.Helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        this.Helper.Events.GameLoop.DayEnding += this.OnDayEnding;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.SaveLoaded -= this.OnSaveLoaded;
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Helper.Events.GameLoop.UpdateTicked -= this.OnUpdateTicked;
        this.Helper.Events.GameLoop.DayEnding -= this.OnDayEnding;
        this.Known.Clear();
        this.TakenLate.Clear();
        Active = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>The player's own quests with a deadline, still to do.</summary>
    private static IEnumerable<Quest> TimedQuests() =>
        Game1.player.questLog.Where(q => q.IsTimedQuest() && q.GetDaysLeft() > 0 && !q.completed.Value && !q.destroy.Value).ToList();

    /// <summary>The special orders under way (shared by the farm).</summary>
    private static IEnumerable<SpecialOrder> RunningOrders() =>
        Game1.player.team.specialOrders.Where(o => o.questState.Value == SpecialOrderStatus.InProgress).ToList();

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e) => this.Remember();

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Remember();
        if (this.MorningNote != null)
        {
            Game1.addHUDMessage(new HUDMessage(this.MorningNote, HUDMessage.newQuest_type));
            this.MorningNote = null;
        }
    }

    /// <summary>What's in the journal at the start of the day isn't "taken today".</summary>
    private void Remember()
    {
        this.Known.Clear();
        this.TakenLate.Clear();
        foreach (Quest quest in Game1.player.questLog)
            this.Known.Add(quest);
        foreach (SpecialOrder order in Game1.player.team.specialOrders)
            this.Known.Add(order);
    }

    /// <summary>Notice the quests and orders taken during the day, and at what time.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !e.IsMultipleOf(15))
            return;
        bool late = Game1.timeOfDay >= this.Settings.GetNumber(CutoffKey, 1200);
        foreach (Quest quest in Game1.player.questLog)
            if (this.Known.Add(quest) && late && quest.IsTimedQuest())
                this.TakenLate.Add(quest);
        foreach (SpecialOrder order in Game1.player.team.specialOrders)
            if (this.Known.Add(order) && late)
                this.TakenLate.Add(order);
    }

    /// <summary>Just before the game takes this day off the deadlines, give it back where it shouldn't count.</summary>
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;
        bool pause = this.IsPauseDay();
        int given = 0;

        foreach (Quest quest in TimedQuests())
        {
            if (pause || this.TakenLate.Contains(quest))
            {
                quest.daysLeft.Value++;
                given++;
            }
        }

        // the special orders' deadline is the farm's: the host moves it, once
        if (Context.IsMainPlayer)
        {
            foreach (SpecialOrder order in RunningOrders())
            {
                if (pause || this.TakenLate.Contains(order))
                {
                    order.dueDate.Value++;
                    given++;
                }
            }
        }

        if (given > 0)
        {
            this.MorningNote = this.Helper.Translation.Get(pause ? "fair-deadlines.festival" : "fair-deadlines.late", new { count = given });
            this.Monitor.Log($"Fair deadlines: gave a day back to {given} quest(s) ({(pause ? "festival day" : "taken late")}).", LogLevel.Trace);
        }
    }
}
