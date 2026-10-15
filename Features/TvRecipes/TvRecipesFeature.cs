using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace PelicanMemory.Features.TvRecipes;

/// <summary>On the cooking show's days, says whether some of its recipes are still missing.</summary>
/// <remarks>
/// The Queen of Sauce teaches a recipe on Sundays and reruns one on Wednesdays, 32 in all over two years
/// (<c>Data/TV/CookingChannel</c>, <c>TV.getWeeklyRecipe</c>). Watching every week is tedious once most are known, and
/// nothing says when they all are. On those mornings, as long as some are missing, a message says how many; once they're
/// all known, one last message says so, then nothing more. The recipes themselves are never named.
/// </remarks>
internal class TvRecipesFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The flag remembering that the "all known" message was shown, per player.</summary>
    private const string DoneKey = "tv-recipes-all-known";

    private readonly PlayerStore Store;

    /// <summary>The message waiting for the player to be able to read it (after the morning's fade).</summary>
    private string? Pending;


    /*********
    ** Public methods
    *********/
    public override string Id => "tv-recipes";

    public TvRecipesFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store)
        : base(helper, monitor, harmony, settings)
    {
        this.Store = store;
    }

    /// <summary>Count the cooking show's recipes, and how many the player knows.</summary>
    internal static (int Known, int Total) CountRecipes(Farmer player)
    {
        List<string> recipes = DataLoader.Tv_CookingChannel(Game1.content).Values
            .Select(entry => entry.Split('/')[0])
            .Distinct()
            .ToList();
        return (recipes.Count(player.cookingRecipes.ContainsKey), recipes.Count);
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        this.Helper.Events.GameLoop.UpdateTicked -= this.OnUpdateTicked;
        this.Pending = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>On a Sunday or Wednesday, prepare the morning's message.</summary>
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Pending = null;
        try
        {
            string day = Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth);
            if (day is not ("Sun" or "Wed"))
                return;

            var (known, total) = CountRecipes(Game1.player);
            if (known < total)
                this.Pending = this.Helper.Translation.Get(day == "Sun" ? "tv-recipes.missing-sunday" : "tv-recipes.missing-wednesday", new { known, total });
            else if (this.Store.Read<string>(Game1.player, DoneKey) is null)
            {
                this.Pending = this.Helper.Translation.Get("tv-recipes.all-known", new { total });
                this.Store.Write(Game1.player, DoneKey, "1");
            }
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to count the cooking show's recipes:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Show the message once the player can see it: after the morning's fade, outside any event.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.Pending is null || !e.IsMultipleOf(30) || !Context.IsPlayerFree || Game1.eventUp || Game1.fadeToBlack)
            return;

        Game1.addHUDMessage(new HUDMessage(this.Pending, HUDMessage.newQuest_type));
        this.Pending = null;
    }
}
