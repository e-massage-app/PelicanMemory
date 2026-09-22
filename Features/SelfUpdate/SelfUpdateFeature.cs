using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using PelicanMemory.Core;
using PelicanMemory.UI;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.SelfUpdate;

/// <summary>Offers to install a newer version of the mod from the title screen, in one click and one restart.</summary>
/// <remarks>
/// The check runs in the background as the game starts, and says nothing at all when there's no update or no
/// connection. The window only appears on the title screen, never in the middle of a game.
/// </remarks>
internal class SelfUpdateFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    private readonly SelfUpdater Updater;

    /// <summary>The version currently running.</summary>
    private readonly string CurrentVersion;

    /// <summary>The versions newer than the running one, once the check has answered.</summary>
    private volatile IReadOnlyList<ChangelogEntry>? NewerVersions;

    /// <summary>Whether the window was already shown since the game started.</summary>
    private bool Shown;


    /*********
    ** Public methods
    *********/
    public override string Id => "self-update";

    public SelfUpdateFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, SelfUpdater updater, ISemanticVersion currentVersion)
        : base(helper, monitor, harmony, settings)
    {
        this.Updater = updater;
        this.CurrentVersion = currentVersion.ToString();
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        this.Helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;

        if (!this.Shown && this.NewerVersions is null)
            this.CheckInBackground();
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.UpdateTicked -= this.OnUpdateTicked;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Ask which newer versions exist, without holding up the game.</summary>
    private void CheckInBackground()
    {
        Task.Run(async () =>
        {
            try
            {
                IReadOnlyList<ChangelogEntry> newer = await this.Updater.GetNewerVersionsAsync();
                if (newer.Count > 0)
                    this.Monitor.Log($"Version {newer[0].Version} is available (running {this.CurrentVersion}).", LogLevel.Info);
                this.NewerVersions = newer;
            }
            catch (Exception ex)
            {
                // offline, or the release server is unreachable: not worth bothering the player about
                this.Monitor.Log($"Couldn't check for updates: {ex.Message}", LogLevel.Trace);
                this.NewerVersions = Array.Empty<ChangelogEntry>();
            }
        });
    }

    /// <summary>Show the window once the title screen has finished its opening animation.</summary>
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.Shown || this.NewerVersions is not { Count: > 0 } versions)
            return;

        if (Game1.activeClickableMenu is not TitleMenu { titleInPosition: true, logoFadeTimer: <= 0 } || TitleMenu.subMenu != null)
            return;

        this.Shown = true;
        TitleMenu.subMenu = new UpdateMenu(this.Helper.Translation, this.Monitor, this.Updater, versions, this.CurrentVersion);
    }
}
