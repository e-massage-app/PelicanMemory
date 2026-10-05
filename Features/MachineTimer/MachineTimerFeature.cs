using HarmonyLib;
using Microsoft.Xna.Framework;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace PelicanMemory.Features.MachineTimer;

/// <summary>Resting the cursor on a working machine says when it will be done.</summary>
/// <remarks>
/// The game counts each machine's remaining minutes but never shows them. The time is worked out the way the game
/// counts it, including its night jump. What's being made is only named once it's done, as the game itself shows it
/// then. Shown only while the cursor is on the machine.
/// </remarks>
internal class MachineTimerFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The auto-grabber, which keeps a chest as its "output" and isn't a machine one waits for.</summary>
    private const string AutoGrabberId = "(BC)165";


    /*********
    ** Public methods
    *********/
    public override string Id => "machine-timer";

    public MachineTimerFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        WorldTooltip.AddProvider(this.GetTip);
    }

    protected override void OnDisable()
    {
        WorldTooltip.RemoveProvider(this.GetTip);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Describe the machine on a tile, or on the tile below for the top half of a tall machine.</summary>
    private WorldTip? GetTip(GameLocation location, Vector2 tile)
    {
        if (location.objects.TryGetValue(tile, out SObject? machine) && this.Describe(machine) is WorldTip tip)
            return tip;

        Vector2 below = tile + new Vector2(0, 1);
        if (location.objects.TryGetValue(below, out SObject? tall) && tall.bigCraftable.Value)
            return this.Describe(tall);

        return null;
    }

    /// <summary>Say when a machine will be done, or nothing if it isn't working.</summary>
    private WorldTip? Describe(SObject machine)
    {
        switch (machine)
        {
            case Cask cask:
                return this.DescribeCask(cask);

            case CrabPot pot:
                return this.DescribeCrabPot(pot);
        }

        MachineData? data = machine.GetMachineData();
        if (data is null || machine.heldObject.Value is not SObject output || machine.QualifiedItemId == AutoGrabberId)
            return null;

        if (machine.readyForHarvest.Value)
            return new WorldTip(machine.DisplayName, this.T("machine-timer.ready", new { item = output.DisplayName }));

        if (!machine.ShouldTimePassForMachine())
            return new WorldTip(machine.DisplayName, this.T("machine-timer.paused"));

        ReadyTime ready = FarmTiming.GetReadyTime(machine.MinutesUntilReady, Game1.timeOfDay, data.OnlyCompleteOvernight);
        return new WorldTip(machine.DisplayName, this.FormatReady(ready));
    }

    /// <summary>Say when a cask's contents reach their next quality, and iridium.</summary>
    private WorldTip? DescribeCask(Cask cask)
    {
        if (cask.heldObject.Value is not SObject contents)
            return null;

        if (contents.Quality >= SObject.bestQuality)
            return new WorldTip(cask.DisplayName, this.T("machine-timer.ready", new { item = contents.DisplayName }));

        int next = cask.GetNextQuality(contents.Quality);
        int toNext = FarmTiming.GetCaskDays(cask.daysToMature.Value, cask.agingRate.Value, cask.GetDaysForQuality(next));
        int toBest = FarmTiming.GetCaskDays(cask.daysToMature.Value, cask.agingRate.Value, cask.GetDaysForQuality(SObject.bestQuality));

        string body = next == SObject.bestQuality
            ? this.T("machine-timer.cask-best", new { count = toBest })
            : this.T("machine-timer.cask-next", new { quality = this.T($"quality.{next}"), count = toNext }) + "\n" + this.T("machine-timer.cask-best", new { count = toBest });
        return new WorldTip(cask.DisplayName, body);
    }

    /// <summary>Say whether a crab pot will have caught something tomorrow.</summary>
    private WorldTip? DescribeCrabPot(CrabPot pot)
    {
        if (pot.readyForHarvest.Value && pot.heldObject.Value is SObject catchItem)
            return new WorldTip(pot.DisplayName, this.T("machine-timer.ready", new { item = catchItem.DisplayName }));

        Farmer owner = Game1.GetPlayer(pot.owner.Value) ?? Game1.MasterPlayer;
        string body = pot.NeedsBait(owner) ? this.T("machine-timer.needs-bait") : this.T("machine-timer.tomorrow");
        return new WorldTip(pot.DisplayName, body);
    }

    /// <summary>Write when a machine will be done: a time today, tomorrow, or in a few days.</summary>
    private string FormatReady(ReadyTime ready)
    {
        string? time = ready.Time is int value ? Game1.getTimeOfDayString(value) : null;

        return (ready.DayOffset, time) switch
        {
            (0, not null) => this.T("machine-timer.today", new { time }),
            (1, null) => this.T("machine-timer.tomorrow"),
            (1, not null) => this.T("machine-timer.tomorrow-at", new { time }),
            (_, null) => this.T("machine-timer.days", new { count = ready.DayOffset }),
            _ => this.T("machine-timer.days-at", new { count = ready.DayOffset, time })
        };
    }

    private string T(string key, object? tokens = null)
    {
        return this.Helper.Translation.Get(key, tokens);
    }
}
