using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>Lists which of the player's containers hold an item, and how many are in each.</summary>
internal class ItemLocationMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int MenuWidth = 700;
    private const int MenuHeight = 540;

    /// <summary>The size of the square the item's own sprite is drawn in.</summary>
    private const int IconSize = 48;

    /// <summary>The size of a chest icon on each row.</summary>
    private const int ChestIconSize = 32;

    private const int RowHeight = 44;

    /// <summary>The distance from the right edge to the right of the count column.</summary>
    private const int CountColumnOffset = 40;

    private static readonly Color ReadyColor = new(40, 120, 60);
    private static readonly Color StoredColor = new(190, 120, 30);
    private static readonly Color GridColor = new(120, 90, 60);

    private readonly ITranslationHelper Translations;
    private readonly string Title;
    private readonly string SpriteId;
    private readonly int InBag;
    private readonly IReadOnlyList<ItemStash> Stashes;

    /// <summary>The first container shown, for scrolling.</summary>
    private int ScrollOffset;

    private int InChests => this.Stashes.Sum(stash => stash.Count);

    private Rectangle ContentArea => new(this.xPositionOnScreen + 32, this.yPositionOnScreen + 160, this.width - 64, this.height - 224);


    /*********
    ** Public methods
    *********/
    public ItemLocationMenu(ITranslationHelper translations, Item item, int inBag, IReadOnlyList<ItemStash> stashes)
        : base(Game1.uiViewport.Width / 2 - MenuWidth / 2, Game1.uiViewport.Height / 2 - MenuHeight / 2, MenuWidth, MenuHeight, showUpperRightCloseButton: true)
    {
        this.Translations = translations;
        this.SpriteId = item.QualifiedItemId;
        this.InBag = inBag;
        this.Stashes = stashes;
        this.Title = translations.Get("storage.title", new { item = item.DisplayName });
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        int maximum = Math.Max(0, this.Stashes.Count - this.GetVisibleRows());

        if (direction > 0 && this.ScrollOffset > 0)
        {
            this.ScrollOffset--;
            Game1.playSound("shiny4");
        }
        else if (direction < 0 && this.ScrollOffset < maximum)
        {
            this.ScrollOffset++;
            Game1.playSound("shiny4");
        }
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
        drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);
        SpriteText.drawString(b, this.Title, this.xPositionOnScreen + 32, this.yPositionOnScreen + 24);

        Rectangle area = this.ContentArea;
        this.DrawSummary(b, area);

        if (this.Stashes.Count == 0)
            Utility.drawTextWithShadow(b, this.Translations.Get("storage.none"), Game1.smallFont, new Vector2(area.X, area.Y + 12), Game1.textColor, 0.9f);
        else
            this.DrawStashes(b, area);

        base.draw(b);
        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the item, what's on you, and what's put away.</summary>
    private void DrawSummary(SpriteBatch b, Rectangle area)
    {
        int y = this.yPositionOnScreen + 84;
        this.DrawItemSprite(b, area.X, y - 8);

        int x = area.X + IconSize + 16;
        Utility.drawTextWithShadow(b, this.Translations.Get("storage.in-bag", new { count = this.InBag }), Game1.smallFont, new Vector2(x, y), this.InBag > 0 ? ReadyColor : Game1.textColor * 0.5f, 1f);
        Utility.drawTextWithShadow(b, this.Translations.Get("storage.in-chests", new { count = this.InChests }), Game1.smallFont, new Vector2(x, y + 28), this.InChests > 0 ? StoredColor : Game1.textColor * 0.5f, 1f);

        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y - 12, area.Width, 2), GridColor * 0.5f);
    }

    /// <summary>Draw one row per container, each chest at the colour the player painted it.</summary>
    private void DrawStashes(SpriteBatch b, Rectangle area)
    {
        int rows = this.GetVisibleRows();
        int y = area.Y + 4;

        foreach (ItemStash stash in this.Stashes.Skip(this.ScrollOffset).Take(rows))
        {
            this.DrawChest(b, stash, area.X + 8, y);

            int textX = area.X + 8 + ChestIconSize + 16;
            this.DrawTruncated(b, stash.DisplayName, Game1.smallFont, textX, y + 4, area.Right - CountColumnOffset - 80 - textX, Game1.textColor, 0.9f);
            this.DrawRightAligned(b, stash.Count.ToString(), area.Right - CountColumnOffset, y + 4, StoredColor, 0.9f);

            y += RowHeight;
            b.Draw(Game1.staminaRect, new Rectangle(area.X, y - 6, area.Width, 1), GridColor * 0.2f);
        }

        if (this.Stashes.Count > rows)
        {
            string hint = this.Translations.Get("storage.scroll", new { shown = Math.Min(this.ScrollOffset + rows, this.Stashes.Count), total = this.Stashes.Count });
            Utility.drawTextWithShadow(b, hint, Game1.smallFont, new Vector2(area.X, this.yPositionOnScreen + this.height - 56), Game1.textColor * 0.6f, 0.8f);
        }
    }

    /// <summary>Get how many container rows fit in the window.</summary>
    private int GetVisibleRows()
    {
        return Math.Max(1, this.ContentArea.Height / RowHeight);
    }

    /// <summary>Draw one chest at the colour the player painted it.</summary>
    private void DrawChest(SpriteBatch b, ItemStash stash, int x, int y)
    {
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(stash.ChestId);
        Rectangle source = data.GetSourceRect();
        if (source.Width <= 0 || source.Height <= 0)
            return;

        float scale = ChestIconSize / (float)Math.Max(source.Width, source.Height);
        b.Draw(data.GetTexture(), new Vector2(x, y), source, stash.ChestColor ?? Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.86f);
    }

    /// <summary>Draw the looked-up item, fitted into a fixed square.</summary>
    private void DrawItemSprite(SpriteBatch b, int x, int y)
    {
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(this.SpriteId);
        Rectangle source = data.GetSourceRect();
        if (source.Width <= 0 || source.Height <= 0)
            return;

        float scale = IconSize / (float)Math.Max(source.Width, source.Height);
        b.Draw(data.GetTexture(), new Vector2(x, y), source, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.86f);
    }

    /// <summary>Draw text, shortened if it would reach the count column.</summary>
    private void DrawTruncated(SpriteBatch b, string text, SpriteFont font, int x, int y, int maxWidth, Color color, float scale)
    {
        if (maxWidth > 0 && font.MeasureString(text).X * scale > maxWidth)
        {
            while (text.Length > 1 && font.MeasureString(text + "...").X * scale > maxWidth)
                text = text[..^1];
            text += "...";
        }

        Utility.drawTextWithShadow(b, text, font, new Vector2(x, y), color, scale);
    }

    /// <summary>Draw text ending at a given x, so numbers line up in a column.</summary>
    private void DrawRightAligned(SpriteBatch b, string text, int right, int y, Color color, float scale)
    {
        float width = Game1.smallFont.MeasureString(text).X * scale;
        Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(right - width, y), color, scale);
    }
}
