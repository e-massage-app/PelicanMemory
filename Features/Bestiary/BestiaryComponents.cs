using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.Bestiary;

/// <summary>A monster's face in the bestiary: drawn as large as fits its square, a silhouette until beaten, darkened and ticked once Gil's goal is done.</summary>
/// <remarks>
/// Named <c>"&lt;key&gt; True"</c> or <c>"&lt;key&gt; False"</c> like the game's own collection icons, so the page draws
/// a beaten monster in full colour and the others as a faint silhouette, exactly as it does for fish.
/// </remarks>
internal class MonsterIcon : ClickableTextureComponent
{
    /// <summary>The monster, or for the family view the goal's ID.</summary>
    public string Key { get; }

    /// <summary>Whether this icon stands for one of Gil's families rather than one monster.</summary>
    public bool IsFamily { get; }

    /// <summary>Whether Gil's goal for its family is done.</summary>
    public bool GoalDone { get; }

    private readonly Color Tint;

    public MonsterIcon(string key, bool isFamily, bool beaten, bool goalDone, Rectangle bounds, Texture2D texture, Rectangle source, Color tint)
        : base($"{key.Replace(' ', '_')} {beaten}", bounds, null, "", texture, source, 1f)
    {
        this.Key = key;
        this.IsFamily = isFamily;
        this.GoalDone = goalDone;
        this.Tint = tint;
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b, Color c, float layerDepth, int frameOffset = 0, int xOffset = 0, int yOffset = 0)
    {
        if (!this.visible || this.sourceRect.Width <= 0 || this.sourceRect.Height <= 0)
            return;

        // the page passes white for a beaten monster and a faint black for the others: keep its silhouette, add the slime colours
        bool beaten = c == Color.White;
        Color color = beaten ? this.Tint : c;
        if (beaten && this.GoalDone)
            color = Color.Lerp(color, Color.Black, 0.45f);

        // whole pixels, as large as fits, centred; grows a little on hover like the game's icons
        int pixel = Math.Max(1, Math.Min(this.bounds.Width / this.sourceRect.Width, this.bounds.Height / this.sourceRect.Height));
        float scale = pixel * (this.scale / this.baseScale);
        Vector2 centre = new(this.bounds.Center.X + xOffset, this.bounds.Center.Y + yOffset);
        b.Draw(this.texture, centre, this.sourceRect, color, 0f, new Vector2(this.sourceRect.Width / 2f, this.sourceRect.Height / 2f), scale, SpriteEffects.None, layerDepth);

        if (beaten && this.GoalDone)
        {
            Rectangle tick = OptionsCheckbox.sourceRectChecked;
            b.Draw(Game1.mouseCursors, new Vector2(this.bounds.Right - tick.Width * 3 + 4, this.bounds.Bottom - tick.Height * 3 + 4), tick, Color.White, 0f, Vector2.Zero, 3f, SpriteEffects.None, layerDepth + 0.01f);
        }
    }
}

/// <summary>The button switching the bestiary between one icon per monster and one per Gil family.</summary>
/// <remarks>Named like an icon (<c>"&lt;name&gt; True"</c>): the page reads the second word of every component it draws.</remarks>
internal class BestiaryViewToggle : ClickableTextureComponent
{
    private readonly string Label;

    public BestiaryViewToggle(Rectangle bounds, string label)
        : base("bestiary-toggle True", bounds, null, label, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), 4f)
    {
        this.Label = label;
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b, Color c, float layerDepth, int frameOffset = 0, int xOffset = 0, int yOffset = 0)
    {
        if (!this.visible)
            return;

        // the game's small framed box, with the label in its font
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, this.sourceRect, this.bounds.X, this.bounds.Y, this.bounds.Width, this.bounds.Height, Color.White, 4f, drawShadow: false, layerDepth);
        Vector2 size = Game1.smallFont.MeasureString(this.Label);
        Vector2 position = new(this.bounds.Center.X - size.X / 2, this.bounds.Center.Y - size.Y / 2 + 2);
        b.DrawString(Game1.smallFont, this.Label, position, Game1.textColor * (this.scale > this.baseScale ? 1f : 0.85f), 0f, Vector2.Zero, 1f, SpriteEffects.None, layerDepth + 0.01f);
    }
}
