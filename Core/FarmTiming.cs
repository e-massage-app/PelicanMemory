using System;
using System.Collections.Generic;
using StardewValley;

namespace PelicanMemory.Core;

/// <summary>When a machine will be ready, as the player thinks of it.</summary>
/// <param name="DayOffset">0 for today, 1 for tomorrow, and so on.</param>
/// <param name="Time">The time of day it finishes, or <c>null</c> if it finishes overnight and is ready on waking.</param>
internal record ReadyTime(int DayOffset, int? Time);

/// <summary>The arithmetic of growing crops and working machines, read from the game's own counters.</summary>
/// <remarks>
/// Plain numbers in, plain numbers out, so every rule can be checked outside the game. The rules mirror the game's
/// code (Stardew Valley 1.6.15): <c>Crop.newDay</c>, <c>HoeDirt.readyForHarvest</c>, <c>Object.minutesElapsed</c> and
/// <c>Utility.CalculateMinutesUntilMorning</c>.
/// </remarks>
internal static class FarmTiming
{
    /*********
    ** Fields
    *********/
    /// <summary>The days in a season.</summary>
    private const int DaysPerSeason = 28;

    /// <summary>The minutes a machine counts from midnight until waking: the game adds them all at once overnight.</summary>
    private const int OvernightMinutes = 400;

    /// <summary>The minutes a machine counts over a whole day and night.</summary>
    private const int MinutesPerDay = 1600;

    /// <summary>The minutes a machine counts between waking (6:00) and 2:00.</summary>
    private const int MinutesAwake = 1200;


    /*********
    ** Public methods
    *********/
    /// <summary>Get how many watered nights a crop needs before it can be harvested.</summary>
    /// <param name="phaseDays">The crop's days per phase, ending with the game's sentinel for the final phase. Speed-Gro and the Agriculturist profession are already counted in.</param>
    /// <param name="currentPhase">The crop's current phase.</param>
    /// <param name="dayOfCurrentPhase">The days spent in that phase, or for a crop regrowing after harvest the days left before it can be picked again.</param>
    /// <param name="fullyGrown">Whether the crop was already harvested once and is regrowing.</param>
    /// <returns>0 if it can be harvested now.</returns>
    public static int GetDaysUntilHarvest(IReadOnlyList<int> phaseDays, int currentPhase, int dayOfCurrentPhase, bool fullyGrown)
    {
        int last = phaseDays.Count - 1;

        // last phase: ready, or regrowing with its own countdown
        if (currentPhase >= last)
            return !fullyGrown || dayOfCurrentPhase <= 0 ? 0 : dayOfCurrentPhase;

        // still growing: the rest of this phase (at least one night, since a phase only advances overnight), then every later phase
        int days = Math.Max(1, phaseDays[currentPhase] - dayOfCurrentPhase);
        for (int i = currentPhase + 1; i < last; i++)
            days += Math.Max(0, phaseDays[i]);
        return days;
    }

    /// <summary>Get whether an outdoor crop will be killed by a change of season before it can be harvested.</summary>
    /// <param name="daysLeft">The watered nights it still needs.</param>
    /// <param name="dayOfMonth">Today's day of the month.</param>
    /// <param name="season">Today's season.</param>
    /// <param name="cropSeasons">The seasons the crop grows in.</param>
    /// <remarks>The season changes before crops grow overnight, so a crop out of season dies on the morning of the 1st.</remarks>
    public static bool WillDieBeforeHarvest(int daysLeft, int dayOfMonth, Season season, IReadOnlyCollection<Season> cropSeasons)
    {
        int day = dayOfMonth;
        int remaining = daysLeft;

        while (day + remaining > DaysPerSeason)
        {
            remaining -= DaysPerSeason - day + 1;
            day = 1;
            season = (Season)(((int)season + 1) % 4);
            if (!Contains(cropSeasons, season))
                return true;
        }

        return false;
    }

    /// <summary>Get when a working machine will be ready, assuming nothing pauses it.</summary>
    /// <param name="minutesUntilReady">The machine's remaining minutes.</param>
    /// <param name="timeOfDay">The current time, like 1430 for 14:30.</param>
    /// <param name="onlyCompleteOvernight">Whether the machine only ever finishes overnight, like incubators.</param>
    /// <remarks>
    /// The game takes 10 minutes off every 10 in-game minutes, and overnight the minutes until 2:00 plus 400 at once,
    /// whatever the time the player went to bed. So from waking, a day counts 1600 minutes, 1200 of them awake.
    /// </remarks>
    public static ReadyTime GetReadyTime(int minutesUntilReady, int timeOfDay, bool onlyCompleteOvernight)
    {
        int minutes = (Math.Max(0, minutesUntilReady) + 9) / 10 * 10;
        int toEndOfDay = ToMinutes(2600) - ToMinutes(timeOfDay);

        // today
        if (minutes <= toEndOfDay)
            return onlyCompleteOvernight ? new ReadyTime(1, null) : new ReadyTime(0, AddMinutes(timeOfDay, minutes));

        // a later day: what's left when the player wakes tomorrow, then day after day
        int left = minutes - toEndOfDay - OvernightMinutes;
        int dayOffset = 1;
        while (left > 0)
        {
            if (left <= MinutesAwake && !onlyCompleteOvernight)
                return new ReadyTime(dayOffset, AddMinutes(600, left));

            left -= MinutesPerDay;
            dayOffset++;
        }

        return new ReadyTime(dayOffset, null);
    }

    /// <summary>Get how many days a cask still needs to reach a quality.</summary>
    /// <param name="daysToMature">The cask's days left before iridium quality.</param>
    /// <param name="agingRate">How many days it ages per night.</param>
    /// <param name="daysLeftAtQuality">The days left before iridium at which the target quality is reached (56 silver, 42 gold, 28... see the game's cask).</param>
    public static int GetCaskDays(float daysToMature, float agingRate, float daysLeftAtQuality)
    {
        if (agingRate <= 0)
            return int.MaxValue;

        return Math.Max(0, (int)Math.Ceiling((daysToMature - daysLeftAtQuality) / agingRate));
    }


    /*********
    ** Private methods
    *********/
    private static bool Contains(IReadOnlyCollection<Season> seasons, Season season)
    {
        foreach (Season entry in seasons)
        {
            if (entry == season)
                return true;
        }
        return false;
    }

    /// <summary>Convert a time like 1430 to minutes since midnight.</summary>
    private static int ToMinutes(int time)
    {
        return time / 100 * 60 + time % 100;
    }

    /// <summary>Add minutes to a time like 1430, keeping the game's format (past midnight is 2400 and up).</summary>
    private static int AddMinutes(int time, int minutes)
    {
        int total = ToMinutes(time) + minutes;
        return total / 60 * 100 + total % 60;
    }
}
