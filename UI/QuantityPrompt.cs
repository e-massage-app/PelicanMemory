using System;
using HarmonyLib;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>Opens the game's quantity window in a state where typing a number does what you expect.</summary>
/// <remarks>
/// The game pre-fills the box with the default value and appends whatever is typed after it, so with the default at 1,
/// typing 37 asks for 137. Here the box starts empty: the number typed is the number used, Enter confirms, and the
/// arrows still work (they start from nothing, so the first click on "+" gives 1).
/// </remarks>
internal static class QuantityPrompt
{
    /// <summary>Create the quantity window.</summary>
    /// <param name="message">The question shown above the box.</param>
    /// <param name="onChosen">Called with the confirmed quantity.</param>
    /// <param name="maximum">The largest quantity allowed.</param>
    /// <param name="price">The price of one, to show the total, or -1 for no price.</param>
    public static NumberSelectionMenu Create(string message, Action<int> onChosen, int maximum, int price = -1)
    {
        NumberSelectionMenu menu = new(
            message: message,
            behaviorOnSelection: (quantity, _, _) => onChosen(quantity),
            price: price,
            minValue: 1,
            maxValue: Math.Max(1, maximum),
            defaultNumber: 1
        );

        if (AccessTools.Field(typeof(NumberSelectionMenu), "numberSelectedBox")?.GetValue(menu) is StardewValley.Menus.TextBox box)
            box.Text = "";

        return menu;
    }
}
