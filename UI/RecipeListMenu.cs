using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using PelicanMemory.Features.RecipeLookup;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>Lists the cooking recipes the player knows which use an item, and where its ingredients are.</summary>
/// <remarks>
/// Everything sits in fixed columns: a sprite column, a name column, then two number columns. Where an ingredient is
/// stored goes on its own line underneath, so nothing can overlap anything else.
/// </remarks>
internal class RecipeListMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int MenuWidth = 900;
    private const int MenuHeight = 600;

    /// <summary>The size of the square each sprite is drawn in.</summary>
    private const int IconSize = 32;

    private const int IngredientRowHeight = 40;
    private const int PlaceRowHeight = 28;
    private const int RecipeSpacing = 20;

    /// <summary>The size of a chest icon on the storage line.</summary>
    private const int ChestIconSize = 22;

    /// <summary>The distance from the right edge to the right of each number column.</summary>
    private const int BagColumnOffset = 260;
    private const int ChestColumnOffset = 40;

    /// <summary>How wide each number column is, which is where its separator sits.</summary>
    private const int ColumnWidth = 150;

    private static readonly Color MissingColor = new(170, 60, 40);
    private static readonly Color ReadyColor = new(40, 120, 60);

    /// <summary>The colour used when something is only available once fetched from a chest.</summary>
    private static readonly Color StoredColor = new(190, 120, 30);

    /// <summary>The colour of the grid lines.</summary>
    private static readonly Color GridColor = new(120, 90, 60);

    private readonly ITranslationHelper Translations;
    private readonly string Title;
    private readonly IReadOnlyList<KnownRecipe> Recipes;

    /// <summary>The first recipe shown, for scrolling.</summary>
    private int ScrollOffset;

    private Rectangle ContentArea => new(this.xPositionOnScreen + 32, this.yPositionOnScreen + 116, this.width - 64, this.height - 180);


    /*********
    ** Public methods
    *********/
    public RecipeListMenu(ITranslationHelper translations, Item item, IReadOnlyList<KnownRecipe> recipes)
        : base(Game1.uiViewport.Width / 2 - MenuWidth / 2, Game1.uiViewport.Height / 2 - MenuHeight / 2, MenuWidth, MenuHeight, showUpperRightCloseButton: true)
    {
        this.Translations = translations;
        this.Recipes = recipes;
        this.Title = translations.Get("recipes.title", new { item = item.DisplayName });
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        int maximum = Math.Max(0, this.Recipes.Count - 1);

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
        this.DrawHeader(b, area);

        if (this.Recipes.Count == 0)
            Utility.drawTextWithShadow(b, this.Translations.Get("recipes.none"), Game1.smallFont, new Vector2(area.X, area.Y + 16), Game1.textColor, 0.9f);
        else
            this.DrawRecipes(b, area);

        base.draw(b);
        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the column headings and the line under them.</summary>
    private void DrawHeader(SpriteBatch b, Rectangle area)
    {
        this.DrawRightAligned(b, this.Translations.Get("recipes.column-bag"), area.Right - BagColumnOffset, area.Y - 36, Game1.textColor * 0.7f, 0.8f);
        this.DrawRightAligned(b, this.Translations.Get("recipes.column-chests"), area.Right - ChestColumnOffset, area.Y - 36, Game1.textColor * 0.7f, 0.8f);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y - 8, area.Width, 2), GridColor * 0.5f);
    }

    /// <summary>Draw the vertical lines which separate the names from the two number columns.</summary>
    private void DrawColumnLines(SpriteBatch b, Rectangle area, int top, int bottom)
    {
        if (bottom <= top)
            return;

        foreach (int right in new[] { area.Right - BagColumnOffset, area.Right - ChestColumnOffset })
            b.Draw(Game1.staminaRect, new Rectangle(right - ColumnWidth, top, 2, bottom - top), GridColor * 0.25f);
    }

    /// <summary>Draw as many recipes as fit, from the scroll position.</summary>
    private void DrawRecipes(SpriteBatch b, Rectangle area)
    {
        int y = area.Y + 12;
        int drawn = 0;

        foreach (KnownRecipe recipe in this.Recipes.Skip(this.ScrollOffset))
        {
            int height = this.GetRecipeHeight(recipe);
            if (drawn > 0 && y + height > area.Bottom)
                break;

            y = this.DrawRecipe(b, recipe, area, y);
            drawn++;
        }

        string hint = this.Translations.Get("recipes.scroll", new { shown = this.ScrollOffset + drawn, total = this.Recipes.Count });
        if (this.Recipes.Count > drawn || this.ScrollOffset > 0)
            Utility.drawTextWithShadow(b, hint, Game1.smallFont, new Vector2(area.X, this.yPositionOnScreen + this.height - 56), Game1.textColor * 0.6f, 0.8f);
    }

    /// <summary>Get how tall a recipe's block is, including the lines saying where ingredients are stored.</summary>
    private int GetRecipeHeight(KnownRecipe recipe)
    {
        int height = 48 + RecipeSpacing;
        foreach (RecipeIngredient ingredient in recipe.Ingredients)
        {
            height += IngredientRowHeight;
            if (this.ShowsPlaces(ingredient))
                height += PlaceRowHeight;
        }
        return height;
    }

    /// <summary>Draw one recipe, and return the next free vertical position.</summary>
    private int DrawRecipe(SpriteBatch b, KnownRecipe recipe, Rectangle area, int y)
    {
        // title row: the cooked dish's sprite, its name, and whether it can be made
        this.DrawSprite(b, recipe.SpriteId, area.X, y);
        int nameX = area.X + IconSize + 16;
        this.DrawTruncated(b, recipe.Name, Game1.dialogueFont, nameX, y - 2, area.Right - BagColumnOffset - 220 - nameX, Game1.textColor, 0.7f);

        // how many you can cook: from the bag alone, and how many once you fetch what's in your chests
        (string ready, Color readyColor) = recipe switch
        {
            { CanMakeNow: > 0 } when recipe.CanMakeWithChests > recipe.CanMakeNow => (
                this.Translations.Get("recipes.ready-total", new { count = recipe.CanMakeNow, total = recipe.CanMakeWithChests }),
                ReadyColor
            ),
            { CanMakeNow: > 0 } => (
                this.Translations.Get("recipes.ready", new { count = recipe.CanMakeNow }),
                ReadyColor
            ),
            { CanMakeWithChests: > 0 } => (
                this.Translations.Get("recipes.ready-with-chests", new { count = recipe.CanMakeWithChests }),
                StoredColor
            ),
            _ => (this.Translations.Get("recipes.not-ready"), MissingColor)
        };
        this.DrawRightAligned(b, ready, area.Right - ChestColumnOffset, y + 4, readyColor, 0.8f);

        y += 48;
        this.DrawColumnLines(b, area, y - 10, y + this.GetRecipeHeight(recipe) - 48 - RecipeSpacing);

        foreach (RecipeIngredient ingredient in recipe.Ingredients)
        {
            this.DrawSprite(b, ingredient.SpriteId, area.X + 24, y);
            Utility.drawTinyDigits(ingredient.Needed, b, new Vector2(area.X + 24 + IconSize - 10, y + IconSize - 14), 2f, 0.87f, Color.AntiqueWhite);

            // green when it's in your bag, amber when it's only in a chest, red when you don't have it at all
            bool enough = ingredient.InBag >= ingredient.Needed;
            bool enoughWithChests = ingredient.InBag + ingredient.InChests >= ingredient.Needed;
            Color color = enough ? ReadyColor : (enoughWithChests ? StoredColor : MissingColor);

            int ingredientX = area.X + 24 + IconSize + 16;
            this.DrawTruncated(b, ingredient.Name, Game1.smallFont, ingredientX, y + 4, area.Right - BagColumnOffset - ColumnWidth - 24 - ingredientX, color, 0.9f);

            this.DrawRightAligned(b, ingredient.InBag.ToString(), area.Right - BagColumnOffset, y + 4, color, 0.9f);
            this.DrawRightAligned(b, ingredient.InChests > 0 ? ingredient.InChests.ToString() : "—", area.Right - ChestColumnOffset, y + 4, ingredient.InChests > 0 ? Game1.textColor : Game1.textColor * 0.4f, 0.9f);

            y += IngredientRowHeight;

            // which chests hold the rest, each drawn in its own colour
            if (this.ShowsPlaces(ingredient))
            {
                this.DrawStashes(b, ingredient, area, y - 8);
                y += PlaceRowHeight;
            }

            // a line under the row, so the eye follows it across to the numbers
            b.Draw(Game1.staminaRect, new Rectangle(area.X + 16, y - 4, area.Width - 32, 1), GridColor * 0.2f);
        }

        return y + RecipeSpacing;
    }

    /// <summary>Draw the chests holding an ingredient, each at its own painted colour so it's recognisable at a glance.</summary>
    private void DrawStashes(SpriteBatch b, RecipeIngredient ingredient, Rectangle area, int y)
    {
        string label = this.Translations.Get("recipes.stored-in");
        int x = area.X + 24 + IconSize + 16;
        int limit = area.Right - BagColumnOffset - ColumnWidth - 24;

        Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2(x, y), Game1.textColor * 0.6f, 0.75f);
        x += (int)(Game1.smallFont.MeasureString(label).X * 0.75f) + 10;

        foreach (ItemStash stash in ingredient.Stashes)
        {
            string text = $"{stash.Count} — {stash.DisplayName}";
            float width = Game1.smallFont.MeasureString(text).X * 0.75f;
            if (x + ChestIconSize + 6 + width > limit)
                break;

            this.DrawChest(b, stash, x, y - 2);
            x += ChestIconSize + 6;

            Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(x, y), Game1.textColor * 0.6f, 0.75f);
            x += (int)width + 18;
        }
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

    /// <summary>Get whether to say where an ingredient is stored: only when it's stored and not already in the bag.</summary>
    private bool ShowsPlaces(RecipeIngredient ingredient)
    {
        return ingredient.InChests > 0 && ingredient.InBag < ingredient.Needed;
    }

    /// <summary>Draw an item sprite fitted into a fixed square, so rows never overlap.</summary>
    private void DrawSprite(SpriteBatch b, string itemId, int x, int y)
    {
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(itemId);
        Rectangle source = data.GetSourceRect();
        if (source.Width <= 0 || source.Height <= 0)
            return;

        float scale = IconSize / (float)Math.Max(source.Width, source.Height);
        Vector2 position = new(
            x + (IconSize - source.Width * scale) / 2f,
            y + (IconSize - source.Height * scale) / 2f
        );

        b.Draw(data.GetTexture(), position, source, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.86f);
    }

    /// <summary>Draw text, shortened with an ellipsis if it would reach the columns on the right.</summary>
    private void DrawTruncated(SpriteBatch b, string text, SpriteFont font, int x, int y, int maxWidth, Color color, float scale)
    {
        if (maxWidth > 0)
        {
            while (text.Length > 1 && font.MeasureString(text).X * scale > maxWidth)
                text = text[..^1];

            if (font.MeasureString(text).X * scale > maxWidth)
                text = "";
        }

        Utility.drawTextWithShadow(b, text, font, new Vector2(x, y), color, scale);
    }

    /// <summary>Draw text ending at a given x, so numbers line up under their heading.</summary>
    private void DrawRightAligned(SpriteBatch b, string text, int right, int y, Color color, float scale)
    {
        float width = Game1.smallFont.MeasureString(text).X * scale;
        Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2(right - width, y), color, scale);
    }
}
