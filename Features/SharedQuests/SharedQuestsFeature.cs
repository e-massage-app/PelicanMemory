using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Quests;

namespace PelicanMemory.Features.SharedQuests;

/// <summary>A letter quest a player finished, with what the other players should get for it.</summary>
/// <param name="Id">The quest's ID in <c>Data/Quests</c>.</param>
/// <param name="Npc">The villager it was for, who also warms up to the other players.</param>
/// <param name="Friendship">The friendship points the game gave for it.</param>
internal record SharedQuest(string Id, string? Npc, int Friendship);

/// <summary>In multiplayer, a request received by letter and done by one player counts for everyone, reward included.</summary>
/// <remarks>
/// Every player gets the same letters ("bring me a pumpkin", the mayor's shorts...) and has to do each one on their own,
/// which makes no sense on a shared farm. Here, when one player finishes such a quest, it's finished for the others
/// too, with the same reward: the money to claim in their journal, and the villager's friendship. If they hadn't read
/// the letter yet, it's taken out of their mailbox, so the quest doesn't come back.
///
/// Only the letter requests (quests 100 to 125): one step, nothing unlocked, no achievement. Story and progression
/// quests stay each player's own, and the Community Center is already shared by the game.
///
/// Each player records the quests they finish in their own save data, which every game can read; each player then
/// credits themselves for what the others finished. So it also works when the partner wasn't connected at the time.
/// </remarks>
internal class SharedQuestsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patches.</summary>
    private static SharedQuestsFeature? Instance;

    /// <summary>The key of the quests a player finished themselves.</summary>
    private const string DoneKey = "shared-quests-done";

    /// <summary>The key of the quests a player was credited for.</summary>
    private const string CreditedKey = "shared-quests-credited";

    /// <summary>The first and last letter request in <c>Data/Quests</c>.</summary>
    private const int FirstQuest = 100;
    private const int LastQuest = 125;

    private readonly PlayerStore Store;

    /// <summary>The letter which gives each quest, read from <c>Data/Mail</c>.</summary>
    private Dictionary<string, string>? Letters;

    /// <summary>Whether the feature itself is completing a quest, so it isn't recorded as done by this player.</summary>
    private static bool Crediting;


    /*********
    ** Public methods
    *********/
    public override string Id => "shared-quests";

    public SharedQuestsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings, PlayerStore store)
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
        this.Helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked;
        this.Prefix(AccessTools.Method(typeof(Quest), nameof(Quest.questComplete)), typeof(SharedQuestsFeature), nameof(Before_QuestComplete));
        this.Postfix(AccessTools.Method(typeof(Quest), nameof(Quest.questComplete)), typeof(SharedQuestsFeature), nameof(After_QuestComplete));
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.OneSecondUpdateTicked -= this.OnOneSecondUpdateTicked;
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Note whether the quest was still open, so only a real completion counts.</summary>
    private static void Before_QuestComplete(Quest __instance, out bool __state)
    {
        __state = !__instance.completed.Value;
    }

    /// <summary>Record a letter request the local player just finished, for the other players to be credited.</summary>
    private static void After_QuestComplete(Quest __instance, bool __state)
    {
        if (Instance is null || !__state || Crediting || !Context.IsWorldReady || !IsShared(__instance))
            return;

        try
        {
            (string? npc, int friendship) = __instance switch
            {
                ItemDeliveryQuest delivery => (delivery.target.Value, 255),
                LostItemQuest lost => (lost.npcName.Value, 250),
                _ => (null, 0)
            };

            List<SharedQuest> done = Instance.Read<SharedQuest>(Game1.player, DoneKey);
            if (done.All(quest => quest.Id != __instance.id.Value))
            {
                done.Add(new SharedQuest(__instance.id.Value, npc, friendship));
                Instance.Store.Write(Game1.player, DoneKey, done);
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to share a finished quest:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get whether a quest is a letter request shared between players.</summary>
    private static bool IsShared(Quest quest)
    {
        return !quest.dailyQuest.Value && IsSharedId(quest.id.Value);
    }

    /// <summary>Get whether a quest ID is one of the letter requests.</summary>
    private static bool IsSharedId(string? id)
    {
        return int.TryParse(id, out int number) && number >= FirstQuest && number <= LastQuest;
    }

    /// <summary>Credit the local player, once a second, for what the other players finished.</summary>
    private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMultiplayer)
            return;

        try
        {
            Farmer me = Game1.player;
            HashSet<string> mine = this.Read<SharedQuest>(me, DoneKey).Select(quest => quest.Id).ToHashSet();
            List<string> credited = this.Read<string>(me, CreditedKey);
            bool changed = false;

            foreach (Farmer other in Game1.getAllFarmers())
            {
                if (other.UniqueMultiplayerID == me.UniqueMultiplayerID)
                    continue;

                foreach (SharedQuest quest in this.Read<SharedQuest>(other, DoneKey))
                {
                    if (!IsSharedId(quest.Id) || mine.Contains(quest.Id) || credited.Contains(quest.Id))
                        continue;

                    this.Credit(me, quest);
                    credited.Add(quest.Id);
                    changed = true;
                }
            }

            if (changed)
                this.Store.Write(me, CreditedKey, credited);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to credit a quest finished by another player:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Give the local player a quest another player finished, with its reward.</summary>
    private void Credit(Farmer me, SharedQuest shared)
    {
        Quest? quest = me.questLog.FirstOrDefault(entry => entry.id.Value == shared.Id);

        // already done on their own, reward claimed or not: nothing more to give
        if (quest?.completed.Value == true)
            return;

        string? letter = this.GetLetter(shared.Id);
        if (quest is null)
        {
            // they read the letter and the quest is gone: they did it themselves before this mod recorded it
            if (letter != null && me.mailReceived.Contains(letter))
                return;

            // not read yet, or not even arrived: the letter won't bring the quest again
            if (letter != null)
            {
                me.mailbox.Remove(letter);
                me.mailReceived.Add(letter);
            }
            me.addQuest(shared.Id);
            quest = me.questLog.FirstOrDefault(entry => entry.id.Value == shared.Id);
            if (quest is null)
                return;
        }

        // an object found for the quest would be stuck in the bag once it's done
        if (quest is LostItemQuest { itemFound.Value: true } lost && ItemRegistry.QualifyItemId(lost.ItemId.Value) is string itemId)
            me.Items.ReduceId(itemId, 1);

        Crediting = true;
        try
        {
            me.completeQuest(shared.Id);
        }
        finally
        {
            Crediting = false;
        }

        if (shared.Npc != null && shared.Friendship > 0 && Game1.getCharacterFromName(shared.Npc) is NPC npc)
            me.changeFriendship(shared.Friendship, npc);

        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("shared-quests.credited", new { quest = quest.GetName() }), HUDMessage.achievement_type));
    }

    /// <summary>Get the letter which gives a quest.</summary>
    private string? GetLetter(string questId)
    {
        if (this.Letters is null)
        {
            this.Letters = new Dictionary<string, string>();
            Regex questInLetter = new(@"%item quest (\d+)", RegexOptions.CultureInvariant);
            foreach ((string letter, string text) in DataLoader.Mail(Game1.content))
            {
                foreach (Match match in questInLetter.Matches(text))
                    this.Letters.TryAdd(match.Groups[1].Value, letter);
            }
        }

        return this.Letters.TryGetValue(questId, out string? id) ? id : null;
    }

    /// <summary>Read a player's list, or an empty one.</summary>
    private List<T> Read<T>(Farmer player, string key)
    {
        return this.Store.Read<List<T>>(player, key) ?? new List<T>();
    }
}
