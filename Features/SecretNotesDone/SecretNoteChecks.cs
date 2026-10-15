using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StardewValley;

namespace PelicanMemory.Features.SecretNotesDone;

/// <summary>Whether what a secret note or journal scrap points to has been done, from what the game itself records.</summary>
/// <remarks>
/// Checked one by one in the game's code (1.6.15): each entry below is a flag the game sets at the exact moment the
/// thing is done. Notes the game keeps no trace of return <c>null</c>, and the player ticks them by hand.
/// The flags are tested with <see cref="Farmer.hasOrWillReceiveMail"/>, since some only land the next morning.
/// </remarks>
internal static class SecretNoteChecks
{
    /*********
    ** Fields
    *********/
    /// <summary>The flag set when each note's thing is done.</summary>
    private static readonly Dictionary<int, string> MailFlags = new()
    {
        [10] = "qiCave",
        [13] = "junimoPlush",
        [15] = "gotPearl",
        [16] = "SecretNote16_done",
        [17] = "SecretNote17_done",
        [18] = "SecretNote18_done",
        [19] = "SecretNote19_done",
        [20] = "SecretNote20_done",
        [21] = "secretNote21_done", // lower-case 's' in the game too
        [22] = "TH_LumberPile",     // the last step of the hunt
        [1002] = "Island_Secret_BuriedTreasure",
        [1004] = "Island_W_BuriedTreasure",
        [1005] = "reachedCaldera",
        [1006] = "Island_W_BuriedTreasure2",
        [1010] = "Island_N_BuriedTreasure"
    };

    /// <summary>The gift tastes a note reveals, written in its text as <c>%revealtaste:npc:item</c>.</summary>
    /// <remarks>Ends where the game's <c>Utility.ParseGiftReveals</c> stops reading.</remarks>
    private static readonly Regex RevealTaste = new(@"%revealtaste:([^:\s#%$\{\^\*]+):([^\s#%$\{\^\*""]+)", RegexOptions.Compiled);


    /// <summary>The gifts found in each note text.</summary>
    private static readonly Dictionary<string, List<(string Npc, string Item)>> GiftsByText = new();


    /*********
    ** Public methods
    *********/
    /// <summary>Get whether a note's thing is done, or <c>null</c> if the game keeps no trace of it.</summary>
    /// <param name="player">The player.</param>
    /// <param name="noteId">The note ID (journal scraps start at 1000).</param>
    /// <param name="noteText">The note's raw text, for the gift notes.</param>
    public static bool? IsDone(Farmer player, int noteId, string? noteText)
    {
        if (MailFlags.TryGetValue(noteId, out string? flag))
            return player.hasOrWillReceiveMail(flag);

        switch (noteId)
        {
            // the maple syrup scene
            case 23:
                return player.eventsSeen.Contains("2120303");

            // the necklace: fished, then given back (the two quests it starts can't be dropped)
            case 25:
                return player.hasOrWillReceiveMail("carolinesNecklace") && !player.hasQuest("128") && !player.hasQuest("129");

            // the flute blocks tune
            case 1009:
                return player.team.collectedNutTracker.Contains("Mermaid");
        }

        // a gift taste note: done once every gift it names was given at least once
        var (given, total) = GetGiftProgress(player, noteText);
        if (total > 0)
            return given == total;

        return null;
    }

    /// <summary>Count the gifts a note reveals, and how many of them the player has given.</summary>
    public static (int Given, int Total) GetGiftProgress(Farmer player, string? noteText)
    {
        if (string.IsNullOrEmpty(noteText))
            return (0, 0);

        // read once per text: the collections tab asks for every icon on every frame
        if (!GiftsByText.TryGetValue(noteText, out var gifts))
        {
            GiftsByText[noteText] = gifts = RevealTaste.Matches(noteText)
                .Select(match => (Npc: match.Groups[1].Value.Trim(), Item: ItemRegistry.GetMetadata(match.Groups[2].Value.Trim())?.LocalItemId ?? match.Groups[2].Value.Trim()))
                .Distinct()
                .ToList();
        }

        // reading the note adds each gift with a count of 0: only a count above 0 is a gift given
        int given = gifts.Count(gift => player.giftedItems.TryGetValue(gift.Npc, out var items) && items.TryGetValue(gift.Item, out int count) && count > 0);
        return (given, gifts.Count);
    }
}
