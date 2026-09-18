using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PelicanMemory.Core;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>The mod's page in the game menu: one checkbox row per feature, with the description shown on hover.</summary>
/// <remarks>Layout, scrolling, sounds and controller snapping mirror the vanilla <see cref="OptionsPage"/>.</remarks>
internal class ModOptionsPage : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int ItemsPerPage = 7;

    /// <summary>The visible row slots.</summary>
    public List<ClickableComponent> optionSlots = new();

    /// <summary>All rows (title + one checkbox per feature).</summary>
    private readonly List<OptionsElement> Options = new();

    private readonly ClickableTextureComponent UpArrow;
    private readonly ClickableTextureComponent DownArrow;
    private readonly ClickableTextureComponent ScrollBar;
    private readonly Rectangle ScrollBarRunner;

    private int CurrentItemIndex;
    private bool Scrolling;

    /// <summary>The row being held with the mouse button down, so sliders can be dragged.</summary>
    private int OptionsSlotHeld = -1;
    private string HoverText = "";


    /*********
    ** Public methods
    *********/
    public ModOptionsPage(int x, int y, int width, int height, FeatureRegistry registry)
        : base(x, y, width, height)
    {
        this.UpArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + width + 16, this.yPositionOnScreen + 64, 44, 48), Game1.mouseCursors, new Rectangle(421, 459, 11, 12), 4f);
        this.DownArrow = new ClickableTextureComponent(new Rectangle(this.xPositionOnScreen + width + 16, this.yPositionOnScreen + height - 64, 44, 48), Game1.mouseCursors, new Rectangle(421, 472, 11, 12), 4f);
        this.ScrollBar = new ClickableTextureComponent(new Rectangle(this.UpArrow.bounds.X + 12, this.UpArrow.bounds.Y + this.UpArrow.bounds.Height + 4, 24, 40), Game1.mouseCursors, new Rectangle(435, 463, 6, 10), 4f);
        this.ScrollBarRunner = new Rectangle(this.ScrollBar.bounds.X, this.UpArrow.bounds.Y + this.UpArrow.bounds.Height + 4, this.ScrollBar.bounds.Width, height - 128 - this.UpArrow.bounds.Height - 8);

        for (int i = 0; i < ItemsPerPage; i++)
        {
            this.optionSlots.Add(new ClickableComponent(new Rectangle(this.xPositionOnScreen + 16, this.yPositionOnScreen + 80 + 4 + i * ((height - 128) / ItemsPerPage) + 16, width - 32, (height - 128) / ItemsPerPage + 4), i.ToString())
            {
                myID = i,
                downNeighborID = i < ItemsPerPage - 1 ? i + 1 : -7777,
                upNeighborID = i > 0 ? i - 1 : -7777,
                fullyImmutable = true
            });
        }

        this.Options.Add(new OptionsElement(registry.Translate("menu.title")));
        foreach (IFeature feature in registry.Features)
        {
            IFeature current = feature;
            this.Options.Add(new FeatureCheckbox(
                label: registry.GetName(current),
                description: registry.GetDescription(current),
                isChecked: registry.IsEnabled(current),
                onToggled: enabled => registry.SetEnabled(current, enabled)
            ));

            // the feature's own settings, like the minimap opacity
            foreach (OptionsElement row in current.CreateOptionRows())
                this.Options.Add(row);
        }
    }

    /// <inheritdoc />
    public override void snapToDefaultClickableComponent()
    {
        base.snapToDefaultClickableComponent();
        this.currentlySnappedComponent = this.getComponentWithID(1);
        this.snapCursorToCurrentSnappedComponent();
    }

    /// <inheritdoc />
    public override void snapCursorToCurrentSnappedComponent()
    {
        if (this.currentlySnappedComponent != null && this.currentlySnappedComponent.myID < this.optionSlots.Count)
            Game1.setMousePosition(this.currentlySnappedComponent.bounds.Left + 48, this.currentlySnappedComponent.bounds.Center.Y - 12);
        else
            base.snapCursorToCurrentSnappedComponent();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (GameMenu.forcePreventClose)
            return;

        if (this.DownArrow.containsPoint(x, y) && this.CurrentItemIndex < this.MaxItemIndex)
        {
            this.DownArrowPressed();
            Game1.playSound("shwip");
        }
        else if (this.UpArrow.containsPoint(x, y) && this.CurrentItemIndex > 0)
        {
            this.UpArrowPressed();
            Game1.playSound("shwip");
        }
        else if (this.ScrollBar.containsPoint(x, y))
            this.Scrolling = true;
        else if (!this.DownArrow.containsPoint(x, y) && x > this.xPositionOnScreen + this.width && x < this.xPositionOnScreen + this.width + 128 && y > this.yPositionOnScreen && y < this.yPositionOnScreen + this.height)
        {
            this.Scrolling = true;
            this.leftClickHeld(x, y);
            this.releaseLeftClick(x, y);
        }

        this.CurrentItemIndex = Math.Max(0, Math.Min(this.MaxItemIndex, this.CurrentItemIndex));

        for (int i = 0; i < this.optionSlots.Count; i++)
        {
            ClickableComponent slot = this.optionSlots[i];
            if (slot.bounds.Contains(x, y) && this.CurrentItemIndex + i < this.Options.Count && this.Options[this.CurrentItemIndex + i].bounds.Contains(x - slot.bounds.X, y - slot.bounds.Y))
            {
                this.Options[this.CurrentItemIndex + i].receiveLeftClick(x - slot.bounds.X, y - slot.bounds.Y);
                this.OptionsSlotHeld = i;
                break;
            }
        }
    }

    /// <inheritdoc />
    public override void leftClickHeld(int x, int y)
    {
        if (GameMenu.forcePreventClose)
            return;

        base.leftClickHeld(x, y);

        // keep dragging a slider
        if (!this.Scrolling && this.OptionsSlotHeld != -1 && this.CurrentItemIndex + this.OptionsSlotHeld < this.Options.Count)
        {
            ClickableComponent slot = this.optionSlots[this.OptionsSlotHeld];
            this.Options[this.CurrentItemIndex + this.OptionsSlotHeld].leftClickHeld(x - slot.bounds.X, y - slot.bounds.Y);
        }

        if (this.Scrolling)
        {
            int oldY = this.ScrollBar.bounds.Y;
            this.ScrollBar.bounds.Y = Math.Min(this.yPositionOnScreen + this.height - 64 - 12 - this.ScrollBar.bounds.Height, Math.Max(y, this.yPositionOnScreen + this.UpArrow.bounds.Height + 20));
            float percent = (y - this.ScrollBarRunner.Y) / (float)this.ScrollBarRunner.Height;
            this.CurrentItemIndex = Math.Min(this.MaxItemIndex, Math.Max(0, (int)(this.Options.Count * percent)));
            this.SetScrollBarToCurrentIndex();
            if (oldY != this.ScrollBar.bounds.Y)
                Game1.playSound("shiny4");
        }
    }

    /// <inheritdoc />
    public override void releaseLeftClick(int x, int y)
    {
        if (GameMenu.forcePreventClose)
            return;

        base.releaseLeftClick(x, y);

        if (this.OptionsSlotHeld != -1 && this.CurrentItemIndex + this.OptionsSlotHeld < this.Options.Count)
        {
            ClickableComponent slot = this.optionSlots[this.OptionsSlotHeld];
            this.Options[this.CurrentItemIndex + this.OptionsSlotHeld].leftClickReleased(x - slot.bounds.X, y - slot.bounds.Y);
        }

        this.OptionsSlotHeld = -1;
        this.Scrolling = false;
    }

    /// <inheritdoc />
    public override void receiveKeyPress(Keys key)
    {
        base.receiveKeyPress(key);

        // let the selected row handle controller input
        if (Game1.options.snappyMenus && Game1.options.gamepadControls && this.currentlySnappedComponent != null)
        {
            int index = this.CurrentItemIndex + this.currentlySnappedComponent.myID;
            if (index >= 0 && index < this.Options.Count)
                this.Options[index].receiveKeyPress(key);
        }
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        if (GameMenu.forcePreventClose)
            return;

        base.receiveScrollWheelAction(direction);
        if (direction > 0 && this.CurrentItemIndex > 0)
        {
            this.UpArrowPressed();
            Game1.playSound("shiny4");
        }
        else if (direction < 0 && this.CurrentItemIndex < this.MaxItemIndex)
        {
            this.DownArrowPressed();
            Game1.playSound("shiny4");
        }

        if (Game1.options.SnappyMenus)
            this.snapCursorToCurrentSnappedComponent();
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        this.HoverText = "";
        if (GameMenu.forcePreventClose)
            return;

        for (int i = 0; i < this.optionSlots.Count; i++)
        {
            int index = this.CurrentItemIndex + i;
            if (index < this.Options.Count && this.optionSlots[i].bounds.Contains(x, y) && this.Options[index] is IDescribedOption option)
            {
                this.HoverText = option.Description;
                break;
            }
        }

        if (this.ScrollBarRunner.Contains(x, y))
            Game1.SetFreeCursorDrag();

        this.UpArrow.tryHover(x, y);
        this.DownArrow.tryHover(x, y);
        this.ScrollBar.tryHover(x, y);
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        b.End();
        b.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp);
        for (int i = 0; i < this.optionSlots.Count; i++)
        {
            int index = this.CurrentItemIndex + i;
            if (index >= 0 && index < this.Options.Count)
                this.Options[index].draw(b, this.optionSlots[i].bounds.X, this.optionSlots[i].bounds.Y, this);
        }
        b.End();
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

        if (!GameMenu.forcePreventClose && this.Options.Count > ItemsPerPage)
        {
            this.UpArrow.draw(b);
            this.DownArrow.draw(b);
            drawTextureBox(b, Game1.mouseCursors, new Rectangle(403, 383, 6, 6), this.ScrollBarRunner.X, this.ScrollBarRunner.Y, this.ScrollBarRunner.Width, this.ScrollBarRunner.Height, Color.White, 4f, drawShadow: false);
            this.ScrollBar.draw(b);
        }

        if (this.HoverText != "")
            drawHoverText(b, Game1.parseText(this.HoverText, Game1.smallFont, 400), Game1.smallFont);
    }


    /*********
    ** Protected methods
    *********/
    /// <inheritdoc />
    protected override void customSnapBehavior(int direction, int oldRegion, int oldID)
    {
        base.customSnapBehavior(direction, oldRegion, oldID);

        // scroll when moving past the bottom/top visible row
        if (oldID == ItemsPerPage - 1 && direction == 2 && this.CurrentItemIndex < this.MaxItemIndex)
        {
            this.DownArrowPressed();
            Game1.playSound("shiny4");
        }
        else if (oldID == 0 && direction == 0 && this.CurrentItemIndex > 0)
        {
            this.UpArrowPressed();
            Game1.playSound("shiny4");
        }
    }


    /*********
    ** Private methods
    *********/
    private int MaxItemIndex => Math.Max(0, this.Options.Count - ItemsPerPage);

    private void DownArrowPressed()
    {
        this.DownArrow.scale = this.DownArrow.baseScale;
        this.CurrentItemIndex++;
        this.SetScrollBarToCurrentIndex();
    }

    private void UpArrowPressed()
    {
        this.UpArrow.scale = this.UpArrow.baseScale;
        this.CurrentItemIndex--;
        this.SetScrollBarToCurrentIndex();
    }

    private void SetScrollBarToCurrentIndex()
    {
        if (this.Options.Count == 0)
            return;

        this.ScrollBar.bounds.Y = this.ScrollBarRunner.Height / Math.Max(1, this.Options.Count - ItemsPerPage + 1) * this.CurrentItemIndex + this.UpArrow.bounds.Bottom + 4;
        if (this.ScrollBar.bounds.Y > this.DownArrow.bounds.Y - this.ScrollBar.bounds.Height - 4)
            this.ScrollBar.bounds.Y = this.DownArrow.bounds.Y - this.ScrollBar.bounds.Height - 4;
    }
}
