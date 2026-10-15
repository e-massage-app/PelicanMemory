using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.Objects;

namespace PelicanMemory.Features.Loadouts;

/// <summary>After a preset: where should the items with no chest of their own go? The player picks the chest; it's
/// remembered, so next time they go back there on their own.</summary>
/// <remarks>Never a chest picked at random (Jordan names his chests). Named chests come first, with their colour.</remarks>
internal class ChooseChestMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int SlotSize = 64;
    private const int ChestRowHeight = 60;
    private const int ChestsPerPage = 10;

    private readonly ITranslationHelper T;
    private readonly List<Item> Items;
    private readonly HashSet<Item> Selected;
    private readonly List<(Chest Chest, string Label, bool Named)> Chests;
    private readonly Action<Item, Chest> OnStored;
    private int Page;
    private string HoverText = "";


    /*********
    ** Public methods
    *********/
    /// <param name="items">The items left in the bag with no chest.</param>
    /// <param name="chests">The farm's chests.</param>
    /// <param name="onStored">Called for each item put in a chest, to remember it.</param>
    public ChooseChestMenu(ITranslationHelper translations, IEnumerable<Item> items, IEnumerable<Chest> chests, Action<Item, Chest> onStored)
        : base(0, 0, 0, 0, showUpperRightCloseButton: true)
    {
        this.T = translations;
        this.Items = items.ToList();
        this.Selected = new HashSet<Item>(this.Items, ReferenceEqualityComparer.Instance);
        this.OnStored = onStored;
        this.Chests = chests
            .Select(chest => (Chest: chest, Name: ChestLabels.Get(chest)))
            .Select(c => (c.Chest, Label: c.Name ?? $"{c.Chest.DisplayName} — {c.Chest.Location?.DisplayName ?? "?"}", Named: c.Name != null))
            .OrderByDescending(c => c.Named)
            .ThenBy(c => c.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        this.Resize();
    }

    /// <inheritdoc />
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.Resize();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);

        // an item: pick it or leave it out
        for (int i = 0; i < this.Items.Count; i++)
        {
            if (this.ItemArea(i).Contains(x, y))
            {
                if (!this.Selected.Remove(this.Items[i]))
                    this.Selected.Add(this.Items[i]);
                Game1.playSound("smallSelect");
                return;
            }
        }

        // a chest: the picked items go there
        foreach (var (area, chest) in this.ChestAreas())
        {
            if (area.Contains(x, y))
            {
                this.StoreSelected(chest);
                return;
            }
        }

        // pages
        if (this.Page > 0 && this.PreviousArea().Contains(x, y))
        {
            this.Page--;
            Game1.playSound("shwip");
        }
        else if ((this.Page + 1) * ChestsPerPage < this.Chests.Count && this.NextArea().Contains(x, y))
        {
            this.Page++;
            Game1.playSound("shwip");
        }
        else if (this.KeepArea().Contains(x, y))
        {
            Game1.playSound("bigDeSelect");
            this.exitThisMenu();
        }
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        this.HoverText = "";
        for (int i = 0; i < this.Items.Count; i++)
        {
            if (this.ItemArea(i).Contains(x, y))
                this.HoverText = this.Items[i].DisplayName;
        }
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
        SpriteText.drawStringWithScrollCenteredAt(b, this.T.Get("loadouts.choose-title"), this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen + 8);

        int left = this.xPositionOnScreen + 56;
        Utility.drawTextWithShadow(b, this.T.Get("loadouts.choose-intro"), Game1.smallFont, new Vector2(left, this.yPositionOnScreen + 104), Game1.textColor, 0.9f);

        // the items, picked ones in full colour
        for (int i = 0; i < this.Items.Count; i++)
        {
            Rectangle area = this.ItemArea(i);
            bool picked = this.Selected.Contains(this.Items[i]);
            b.Draw(Game1.menuTexture, area, Game1.getSourceRectForStandardTileSheet(Game1.menuTexture, 10), Color.White);
            this.Items[i].drawInMenu(b, new Vector2(area.X, area.Y), 1f, picked ? 1f : 0.35f, 0.9f);
            if (picked)
                b.Draw(Game1.mouseCursors, new Vector2(area.Right - 28, area.Bottom - 28), OptionsCheckbox.sourceRectChecked, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, 0.95f);
        }

        // the chests
        Utility.drawTextWithShadow(b, this.T.Get("loadouts.choose-chest"), Game1.smallFont, new Vector2(left, this.ChestsTop() - 40), Game1.textColor, 0.9f);
        foreach (var (area, chest) in this.ChestAreas())
        {
            var entry = this.Chests.First(c => ReferenceEquals(c.Chest, chest));
            bool hovered = area.Contains(Game1.getMouseX(), Game1.getMouseY());
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), area.X, area.Y, area.Width, area.Height, hovered ? Color.Wheat : Color.White, 4f, false);

            var data = ItemRegistry.GetDataOrErrorItem(chest.QualifiedItemId);
            Rectangle source = data.GetSourceRect();
            float scale = 40f / Math.Max(source.Width, source.Height);
            Color tint = chest.playerChoiceColor.Value == Color.Black ? Color.White : chest.playerChoiceColor.Value;
            b.Draw(data.GetTexture(), new Vector2(area.X + 12, area.Y + (area.Height - source.Height * scale) / 2), source, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.9f);
            DrawTruncated(b, entry.Label, area.X + 60, area.Y + 16, area.Width - 72, entry.Named ? Game1.textColor : Game1.textColor * 0.75f);
        }

        if (this.Page > 0)
            b.Draw(Game1.mouseCursors, this.PreviousArea(), new Rectangle(352, 495, 12, 11), Color.White);
        if ((this.Page + 1) * ChestsPerPage < this.Chests.Count)
            b.Draw(Game1.mouseCursors, this.NextArea(), new Rectangle(365, 495, 12, 11), Color.White);

        Rectangle keep = this.KeepArea();
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), keep.X, keep.Y, keep.Width, keep.Height, keep.Contains(Game1.getMouseX(), Game1.getMouseY()) ? Color.Wheat : Color.White, 4f, false);
        string keepLabel = this.T.Get("loadouts.choose-keep");
        Vector2 size = Game1.smallFont.MeasureString(keepLabel);
        Utility.drawTextWithShadow(b, keepLabel, Game1.smallFont, new Vector2(keep.Center.X - size.X / 2, keep.Center.Y - size.Y / 2 + 2), Game1.textColor);

        base.draw(b);
        if (this.HoverText.Length > 0)
            IClickableMenu.drawHoverText(b, this.HoverText, Game1.smallFont);
        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    private void Resize()
    {
        int itemRows = Math.Max(1, (this.Items.Count + 11) / 12);
        int chestRows = (Math.Min(ChestsPerPage, this.Chests.Count) + 1) / 2;
        this.width = Math.Min(1000, Game1.uiViewport.Width - 32);
        this.height = 104 + 40 + itemRows * (SlotSize + 8) + 56 + chestRows * (ChestRowHeight + 8) + 120;
        this.xPositionOnScreen = Game1.uiViewport.Width / 2 - this.width / 2;
        this.yPositionOnScreen = Math.Max(16, Game1.uiViewport.Height / 2 - this.height / 2);
        this.initializeUpperRightCloseButton();
    }

    private Rectangle ItemArea(int index)
    {
        return new Rectangle(this.xPositionOnScreen + 56 + index % 12 * (SlotSize + 8), this.yPositionOnScreen + 144 + index / 12 * (SlotSize + 8), SlotSize, SlotSize);
    }

    private int ChestsTop()
    {
        int itemRows = Math.Max(1, (this.Items.Count + 11) / 12);
        return this.yPositionOnScreen + 144 + itemRows * (SlotSize + 8) + 56;
    }

    /// <summary>The chests of the current page, two columns.</summary>
    private IEnumerable<(Rectangle Area, Chest Chest)> ChestAreas()
    {
        int columnWidth = (this.width - 112 - 16) / 2;
        int top = this.ChestsTop();
        int i = 0;
        foreach (var entry in this.Chests.Skip(this.Page * ChestsPerPage).Take(ChestsPerPage))
        {
            yield return (new Rectangle(this.xPositionOnScreen + 56 + i % 2 * (columnWidth + 16), top + i / 2 * (ChestRowHeight + 8), columnWidth, ChestRowHeight), entry.Chest);
            i++;
        }
    }

    private Rectangle PreviousArea() => new(this.xPositionOnScreen + 56, this.yPositionOnScreen + this.height - 92, 48, 44);
    private Rectangle NextArea() => new(this.xPositionOnScreen + this.width - 104, this.yPositionOnScreen + this.height - 92, 48, 44);

    private Rectangle KeepArea()
    {
        int w = (int)Game1.smallFont.MeasureString(this.T.Get("loadouts.choose-keep")).X + 40;
        return new Rectangle(this.xPositionOnScreen + this.width / 2 - w / 2, this.yPositionOnScreen + this.height - 100, w, 56);
    }

    /// <summary>Put the picked items in a chest, remember it for each, and close once nothing is left.</summary>
    private void StoreSelected(Chest chest)
    {
        if (this.Selected.Count == 0)
        {
            Game1.playSound("cancel");
            return;
        }

        int stored = 0;
        foreach (Item item in this.Items.Where(this.Selected.Contains).ToList())
        {
            int slot = Game1.player.Items.IndexOf(item);
            if (slot < 0)
            {
                this.Items.Remove(item);   // no longer in the bag (used, dropped…)
                continue;
            }
            Item? rest = chest.addItem(item);
            Game1.player.Items[slot] = rest;
            if (rest is null)
            {
                stored++;
                this.OnStored(item, chest);
                this.Items.Remove(item);
                this.Selected.Remove(item);
            }
        }

        string label = this.Chests.First(c => ReferenceEquals(c.Chest, chest)).Label;
        if (stored > 0)
        {
            Game1.playSound("Ship");
            Game1.addHUDMessage(new HUDMessage(this.T.Get("loadouts.choose-done", new { count = stored, chest = label })));
        }
        else
        {
            Game1.playSound("cancel");
            Game1.addHUDMessage(new HUDMessage(this.T.Get("loadouts.choose-full", new { chest = label }), HUDMessage.error_type));
        }

        if (this.Items.Count == 0)
            this.exitThisMenu();
        else
            this.Resize();
    }

    private static void DrawTruncated(SpriteBatch b, string text, int x, int y, int maxWidth, Color color)
    {
        SpriteFont font = Game1.smallFont;
        if (font.MeasureString(text).X > maxWidth)
        {
            while (text.Length > 1 && font.MeasureString(text + "...").X > maxWidth)
                text = text[..^1];
            text = text.TrimEnd() + "...";
        }
        Utility.drawTextWithShadow(b, text, font, new Vector2(x, y), color);
    }
}
