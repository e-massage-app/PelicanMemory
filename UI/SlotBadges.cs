using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>Where a badge is drawn on an inventory slot.</summary>
internal enum BadgeCorner
{
    TopLeft,
    TopRight
}

/// <summary>A small sprite drawn on an inventory slot.</summary>
/// <param name="Texture">The texture to draw from.</param>
/// <param name="Source">The sprite within the texture.</param>
/// <param name="Scale">The scale to draw it at.</param>
internal record Badge(Texture2D Texture, Rectangle Source, float Scale = 2f)
{
    /// <summary>The vanilla ticked checkbox, used for anything already handed in.</summary>
    public static Badge Done => new(Game1.mouseCursors, OptionsCheckbox.sourceRectChecked, 2.5f);
}

/// <summary>Draws small badges on inventory slots, so a status is visible without hovering each item.</summary>
internal static class SlotBadges
{
    /// <summary>Draw a badge on every slot whose item has one.</summary>
    /// <param name="b">The sprite batch being drawn.</param>
    /// <param name="menu">The inventory being drawn.</param>
    /// <param name="getBadge">Get the badge for an item, or <c>null</c> for none.</param>
    /// <param name="corner">Where to draw the badge within the slot.</param>
    public static void Draw(SpriteBatch b, InventoryMenu menu, Func<Item, Badge?> getBadge, BadgeCorner corner)
    {
        for (int i = 0; i < menu.inventory.Count && i < menu.actualInventory.Count; i++)
        {
            Item item = menu.actualInventory[i];
            if (item is null)
                continue;

            Badge? badge = getBadge(item);
            if (badge is null)
                continue;

            Rectangle slot = menu.inventory[i].bounds;
            int width = (int)(badge.Source.Width * badge.Scale);
            int height = (int)(badge.Source.Height * badge.Scale);
            Vector2 position = new(
                corner == BadgeCorner.TopLeft ? slot.X + 2 : slot.Right - width - 2,
                slot.Y + 2
            );

            // a dark outline, so the badge reads on any item sprite
            b.Draw(Game1.staminaRect, new Rectangle((int)position.X - 2, (int)position.Y - 2, width + 4, height + 4), Color.Black * 0.45f);
            b.Draw(badge.Texture, position, badge.Source, Color.White, 0f, Vector2.Zero, badge.Scale, SpriteEffects.None, 1f);
        }
    }
}
