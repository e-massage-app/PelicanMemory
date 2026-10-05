using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PelicanMemory.Features.ItemSearch;

/// <summary>A name the search can find, with what it points to.</summary>
/// <param name="Id">What the name belongs to, such as a qualified item ID.</param>
/// <param name="Name">The name as displayed.</param>
internal record SearchEntry(string Id, string Name);

/// <summary>Finds names from a few typed letters, the way a player expects: no matter the accents or capitals.</summary>
/// <remarks>Plain text only, with no game state, so it can be checked outside the game.</remarks>
internal static class NameMatcher
{
    /*********
    ** Public methods
    *********/
    /// <summary>Get the entries whose name contains what was typed, best matches first.</summary>
    /// <param name="entries">The names which may be found.</param>
    /// <param name="query">What the player typed.</param>
    /// <param name="limit">The most entries to return.</param>
    public static IReadOnlyList<SearchEntry> Find(IEnumerable<SearchEntry> entries, string query, int limit)
    {
        string wanted = Normalize(query);
        if (wanted.Length == 0)
            return Array.Empty<SearchEntry>();

        return entries
            .Select(entry => (Entry: entry, Rank: GetRank(Normalize(entry.Name), wanted)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Entry.Name.Length)
            .ThenBy(match => match.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(match => match.Entry)
            .Take(limit)
            .ToList();
    }

    /// <summary>Reduce text to what a player means when typing it: lower case, no accents, plain apostrophes and spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        // ligatures typed as two letters: "oeuf" must find « Œuf »
        text = text
            .Replace("œ", "oe").Replace("Œ", "oe")
            .Replace("æ", "ae").Replace("Æ", "ae")
            .Replace('’', '\'').Replace('‘', '\'');

        StringBuilder result = new(text.Length);
        bool lastWasSpace = true;
        foreach (char ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                    result.Append(' ');
                lastWasSpace = true;
                continue;
            }

            result.Append(char.ToLowerInvariant(ch));
            lastWasSpace = false;
        }

        return result.ToString().TrimEnd();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Rank how well a name matches: the start of the name, then the start of a word, then anywhere; -1 if not at all.</summary>
    private static int GetRank(string name, string wanted)
    {
        int index = name.IndexOf(wanted, StringComparison.Ordinal);
        if (index < 0)
            return -1;
        if (index == 0)
            return 0;

        // the start of a later word, so "lune" finds « Pierre de lune » before a name which merely contains it
        for (; index > 0; index = name.IndexOf(wanted, index + 1, StringComparison.Ordinal))
        {
            if (!char.IsLetterOrDigit(name[index - 1]))
                return 1;
        }

        return 2;
    }
}
