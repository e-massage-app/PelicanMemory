using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;

namespace PelicanMemory.Features.PauseTogether;

/// <summary>In multiplayer, time stops while every player has a menu open, like a menu does in single-player.</summary>
/// <remarks>
/// Alone, opening the inventory or a chest stops the clock; together, nothing ever does, so days go by much faster. The
/// game already has the rule "time stops when all players want it to", but only turns it on for split-screen on one
/// computer. This turns it on for online games too: each player says whether they'd pause if they were alone (a menu,
/// a cutscene...), and the host stops the clock only when everyone does. One player browsing a chest while the other
/// works changes nothing.
///
/// Only when every player has this mod (an old version would never report its menus), and never with a dedicated
/// server, whose host isn't a real player. The host's own "/pause" chat command is separate and still works.
///
/// Dungeon mode (on by default): underground (the mines, the Skull Cavern, the quarry mine, the volcano), one player is
/// enough. When the one underground would pause alone (a menu, the "eat it?" question, a dialogue, a cutscene, eating),
/// the clock stops for everyone; and while a menu or dialogue is open there, monsters can't hurt them, as in
/// single-player where a menu freezes the world (the game already makes a player unhittable while eating). The other
/// player can still move during those stops: Jordan's call, they play fair.
/// </remarks>
internal class PauseTogetherFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static PauseTogetherFeature? Instance;

    /// <summary>The first version of the mod which reports its player's menus.</summary>
    private static readonly ISemanticVersion MinimumVersion = new SemanticVersion("1.9.0");

    /// <summary>The setting for dungeon mode.</summary>
    private const string DungeonKey = "pause-together.dungeon";

    private readonly string ModId;


    /*********
    ** Public methods
    *********/
    public override string Id => "pause-together";

    public PauseTogetherFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, string modId)
        : base(helper, monitor, harmony, settings)
    {
        this.ModId = modId;
    }


    /*********
    ** Protected methods
    *********/
    /// <inheritdoc />
    public override IEnumerable<StardewValley.Menus.OptionsElement> CreateOptionRows()
    {
        yield return new UI.FeatureCheckbox(
            label: this.Helper.Translation.Get("option.pause-dungeon"),
            description: this.Helper.Translation.Get("option.pause-dungeon.description"),
            isChecked: this.Settings.GetFlag(DungeonKey, true),
            onToggled: value => this.Settings.SetFlag(DungeonKey, value)
        );
    }

    /// <summary>Get whether a place is underground: the mines' levels (Skull Cavern and quarry mine included) or the volcano.</summary>
    internal static bool IsUnderground(GameLocation? location)
    {
        return location is StardewValley.Locations.MineShaft or StardewValley.Locations.VolcanoDungeon;
    }

    protected override void OnEnable()
    {
        Instance = this;
        this.Transpiler(AccessTools.Method(typeof(Game1), "Update", new[] { typeof(Microsoft.Xna.Framework.GameTime) }), typeof(PauseTogetherFeature), nameof(Transpile_Update));
        this.Postfix(AccessTools.Method(typeof(Game1), "Update", new[] { typeof(Microsoft.Xna.Framework.GameTime) }), typeof(PauseTogetherFeature), nameof(After_Update));
        this.Prefix(AccessTools.Method(typeof(Farmer), nameof(Farmer.takeDamage)), typeof(PauseTogetherFeature), nameof(Before_TakeDamage));
    }

    protected override void OnDisable()
    {
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Turn on the game's split-screen pause rule for online games.</summary>
    /// <remarks>
    /// The game's update asks twice whether the game is split-screen only: once for each player to report whether they
    /// would pause on their own, once for the host to apply "everyone wants a pause". Both questions are answered by
    /// this feature instead. If the game's code isn't the expected one, nothing is changed.
    /// </remarks>
    private static IEnumerable<CodeInstruction> Transpile_Update(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        MethodInfo isLocal = AccessTools.Method(typeof(LocalMultiplayer), nameof(LocalMultiplayer.IsLocalMultiplayer));
        List<int> calls = code.Select((instruction, index) => (instruction, index)).Where(pair => pair.instruction.Calls(isLocal)).Select(pair => pair.index).ToList();

        if (calls.Count != 2)
        {
            Instance?.Monitor.Log($"Can't turn on the shared pause: the game's update has {calls.Count} split-screen checks instead of the 2 expected (probably a game update).", LogLevel.Warn);
            return code;
        }

        // replace the calls in place, keeping their labels
        code[calls[0]].opcode = OpCodes.Call;
        code[calls[0]].operand = AccessTools.Method(typeof(PauseTogetherFeature), nameof(ReportOwnPause));
        code[calls[1]].opcode = OpCodes.Call;
        code[calls[1]].operand = AccessTools.Method(typeof(PauseTogetherFeature), nameof(UseSharedPause));
        return code;
    }

    /// <summary>Dungeon mode, on the host: one player underground who would pause alone stops the clock for everyone.</summary>
    private static void After_Update()
    {
        if (Instance is null || !Context.IsWorldReady || !Game1.IsMultiplayer || !Game1.IsMasterGame || Game1.netWorldState.Value.IsTimePaused)
            return;

        try
        {
            if (!Instance.Settings.GetFlag(DungeonKey, true) || !Instance.CanPauseTogether())
                return;
            if (Game1.getOnlineFarmers().Any(farmer => farmer.requestingTimePause.Value && IsUnderground(farmer.currentLocation)))
                Game1.netWorldState.Value.IsTimePaused = true;
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to apply the dungeon pause:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Dungeon mode, on each player's own game: no damage underground while a menu or dialogue is open, as
    /// in single-player where it freezes the world.</summary>
    private static bool Before_TakeDamage(Farmer __instance)
    {
        if (Instance is null || !Game1.IsMultiplayer || !__instance.IsLocalPlayer || !IsUnderground(__instance.currentLocation))
            return true;
        if (Game1.activeClickableMenu is null or StardewValley.Menus.BobberBar || !Instance.Settings.GetFlag(DungeonKey, true))
            return true;
        return false;
    }

    /// <summary>Every player says whether they would pause if they were alone, as split-screen players do.</summary>
    /// <param name="isLocalOnly">The game's own answer, unused: the report is always made, and only the host reads it.</param>
    private static bool ReportOwnPause(bool isLocalOnly)
    {
        return true;
    }

    /// <summary>The host applies "time stops when everyone wants it to" online too, when it's safe.</summary>
    /// <param name="isLocalOnly">The game's own answer: true in split-screen only, where it already applies the rule.</param>
    private static bool UseSharedPause(bool isLocalOnly)
    {
        return isLocalOnly || (Instance?.CanPauseTogether() ?? false);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get whether every online player reports their menus, so the shared pause can't get stuck.</summary>
    private bool CanPauseTogether()
    {
        if (Game1.HasDedicatedHost)
            return false;

        foreach (Farmer farmer in Game1.getOnlineFarmers())
        {
            if (farmer.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                continue;

            IMultiplayerPeerMod? mod = this.Helper.Multiplayer.GetConnectedPlayer(farmer.UniqueMultiplayerID)?.GetMod(this.ModId);
            if (mod is null || mod.Version.IsOlderThan(MinimumVersion))
                return false;
        }

        return true;
    }
}
