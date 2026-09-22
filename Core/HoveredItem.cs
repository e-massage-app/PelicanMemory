using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Core;

/// <summary>Finds the item the cursor is over, whichever menu is open.</summary>
/// <remarks>Menus keep it in a private field rather than exposing it, under one of two names depending on the menu.</remarks>
internal static class HoveredItem
{
    /// <summary>Get the item under the cursor, or <c>null</c> if there is none.</summary>
    public static Item? Get(IModHelper helper)
    {
        IClickableMenu? menu = Game1.activeClickableMenu;
        if (menu is GameMenu gameMenu)
            menu = gameMenu.GetCurrentPage();

        if (menu is null)
            return null;

        foreach (string field in new[] { "hoveredItem", "hoverItem" })
        {
            Item? item = helper.Reflection.GetField<Item>(menu, field, required: false)?.GetValue();
            if (item != null)
                return item;
        }

        return null;
    }
}
