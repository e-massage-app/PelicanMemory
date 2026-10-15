using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace PelicanMemory.Features.Loadouts;

/// <summary>The five presets: apply one, save the current outfit in a slot, rename, delete.</summary>
internal class LoadoutsMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int RowHeight = 104;
    private const int ButtonHeight = 52;

    /// <summary>The width kept on the left of each row for the name and the worn items; the buttons start after it.</summary>
    private const int InfoWidth = 380;

    /// <summary>Where the rows start, below the title.</summary>
    private const int RowsTop = 112;

    /// <summary>The room under the rows for the explanation.</summary>
    private const int FooterHeight = 120;

    private readonly LoadoutsFeature Feature;
    private readonly ITranslationHelper T;

    /// <summary>A button of a row.</summary>
    private record Button(Rectangle Area, string Label, bool Enabled, Action OnClick, string? Hover = null);

    private readonly List<Button> Buttons = new();
    private string HoverText = "";


    /*********
    ** Public methods
    *********/
    public LoadoutsMenu(LoadoutsFeature feature, ITranslationHelper translations)
        : base(0, 0, Math.Min(1180, Game1.uiViewport.Width - 32), RowsTop + LoadoutsFeature.MaxLoadouts * RowHeight + FooterHeight, showUpperRightCloseButton: true)
    {
        this.xPositionOnScreen = Game1.uiViewport.Width / 2 - this.width / 2;
        this.yPositionOnScreen = Game1.uiViewport.Height / 2 - this.height / 2;
        this.initializeUpperRightCloseButton();
        this.Feature = feature;
        this.T = translations;
        this.Layout();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        foreach (Button button in this.Buttons)
        {
            if (!button.Area.Contains(x, y))
                continue;
            if (!button.Enabled)
            {
                Game1.playSound("cancel");
                return;
            }
            Game1.playSound("smallSelect");
            button.OnClick();
            return;
        }
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        this.HoverText = this.Buttons.FirstOrDefault(button => button.Area.Contains(x, y))?.Hover ?? "";
    }

    /// <inheritdoc />
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.xPositionOnScreen = Game1.uiViewport.Width / 2 - this.width / 2;
        this.yPositionOnScreen = Game1.uiViewport.Height / 2 - this.height / 2;
        this.initializeUpperRightCloseButton();
        this.Layout();
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showMenuBackground)
            b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
        else
            this.drawBackground(b);

        Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true);
        SpriteText.drawStringWithScrollCenteredAt(b, this.T.Get("loadouts.title"), this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen + 8);

        IReadOnlyList<Loadout?> loadouts = this.Feature.GetLoadouts();
        int left = this.xPositionOnScreen + 56;
        for (int i = 0; i < LoadoutsFeature.MaxLoadouts; i++)
        {
            int top = this.RowTop(i);
            Loadout? loadout = loadouts[i];

            // the row's frame, like the game's inner boxes
            IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), left - 16, top - 8, this.width - 80, RowHeight - 8, Color.White, 1f, false);

            // the name, shortened to its column
            string name = $"{i + 1}. {loadout?.Name ?? this.T.Get("loadouts.empty")}";
            DrawTruncated(b, name, Game1.dialogueFont, left, top + 2, InfoWidth - 8, loadout is null ? Game1.textColor * 0.45f : Game1.textColor, 0.8f);

            // what it holds: the worn items, then how many bag slots
            if (loadout != null)
            {
                int x = left;
                foreach (LoadoutItem? worn in Loadout.EquipmentSlots.Select(slot => loadout.Equipment.GetValueOrDefault(slot)).Concat(loadout.Equipment.Where(p => p.Key.StartsWith("Trinket", StringComparison.Ordinal)).Select(p => p.Value)))
                {
                    if (worn is null)
                        continue;
                    var data = ItemRegistry.GetDataOrErrorItem(worn.Id);
                    DrawIcon(b, data.GetTexture(), data.GetSourceRect(), x, top + 48);
                    x += 36;
                }
                int count = loadout.Slots.Count(slot => slot != null);
                DrawTruncated(b, this.T.Get("loadouts.bag-count", new { count }), Game1.smallFont, x + 6, top + 54, left + InfoWidth - x - 6, Game1.textColor * 0.7f, 0.8f);
            }
        }

        foreach (Button button in this.Buttons)
            this.DrawButton(b, button);

        // the rule, under the rows, inside the frame
        float ruleScale = 0.85f;
        string rule = Game1.parseText(this.T.Get("loadouts.rule"), Game1.smallFont, (int)((this.width - 128) / ruleScale));
        Utility.drawTextWithShadow(b, rule, Game1.smallFont, new Vector2(left, this.RowTop(LoadoutsFeature.MaxLoadouts) + 8), Game1.textColor * 0.7f, ruleScale);

        base.draw(b);
        if (this.HoverText.Length > 0)
            IClickableMenu.drawHoverText(b, Game1.parseText(this.HoverText, Game1.smallFont, 400), Game1.smallFont);
        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    private int RowTop(int index)
    {
        return this.yPositionOnScreen + RowsTop + index * RowHeight;
    }

    /// <summary>Build the buttons of each row from the presets as they are now.</summary>
    private void Layout()
    {
        this.Buttons.Clear();
        IReadOnlyList<Loadout?> loadouts = this.Feature.GetLoadouts();
        bool onFarm = this.Feature.CanApplyHere();
        int right = this.xPositionOnScreen + this.width - 72;

        for (int i = 0; i < LoadoutsFeature.MaxLoadouts; i++)
        {
            int index = i;
            Loadout? loadout = loadouts[i];
            int y = this.RowTop(i) + (RowHeight - 8 - ButtonHeight) / 2 - 8 + 2;
            int x = right;

            Button Add(string label, bool enabled, Action onClick, string? hover = null)
            {
                int w = (int)Game1.smallFont.MeasureString(label).X + 28;
                x -= w + 8;
                Button button = new(new Rectangle(x, y, w, ButtonHeight), label, enabled, onClick, hover);
                this.Buttons.Add(button);
                return button;
            }

            // right to left: delete, rename, save, apply
            if (loadout != null)
            {
                Add(this.T.Get("loadouts.delete"), true, () => this.Feature.AskDelete(index));
                Add(this.T.Get("loadouts.rename"), true, () => this.Feature.AskRename(index));
            }
            Add(this.T.Get(loadout is null ? "loadouts.save" : "loadouts.overwrite"), true, () => this.Feature.AskSave(index), this.T.Get("loadouts.save-hint"));
            if (loadout != null)
                Add(this.T.Get("loadouts.apply"), onFarm, () => this.Feature.Apply(index), onFarm ? null : (string)this.T.Get("loadouts.farm-only"));
        }
    }

    /// <summary>Draw a button in the game's small framed box, faded when it can't be used.</summary>
    private void DrawButton(SpriteBatch b, Button button)
    {
        bool hovered = button.Area.Contains(Game1.getMouseX(), Game1.getMouseY());
        Color tint = !button.Enabled ? Color.Gray * 0.7f : hovered ? Color.Wheat : Color.White;
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), button.Area.X, button.Area.Y, button.Area.Width, button.Area.Height, tint, 4f, false);
        Vector2 size = Game1.smallFont.MeasureString(button.Label);
        Utility.drawTextWithShadow(b, button.Label, Game1.smallFont, new Vector2(button.Area.Center.X - size.X / 2, button.Area.Center.Y - size.Y / 2 + 2), Game1.textColor * (button.Enabled ? 1f : 0.5f));
    }

    /// <summary>Draw text, shortened with dots if it would run past a width.</summary>
    private static void DrawTruncated(SpriteBatch b, string text, SpriteFont font, int x, int y, int maxWidth, Color color, float scale)
    {
        if (maxWidth <= 0)
            return;
        if (font.MeasureString(text).X * scale > maxWidth)
        {
            while (text.Length > 1 && font.MeasureString(text + "...").X * scale > maxWidth)
                text = text[..^1];
            text = text.TrimEnd() + "...";
        }
        Utility.drawTextWithShadow(b, text, font, new Vector2(x, y), color, scale);
    }

    /// <summary>Draw an item's sprite fitted into a 32-pixel square.</summary>
    private static void DrawIcon(SpriteBatch b, Texture2D texture, Rectangle source, int x, int y)
    {
        float scale = 32f / Math.Max(source.Width, source.Height);
        b.Draw(texture, new Vector2(x, y), source, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.9f);
    }
}
