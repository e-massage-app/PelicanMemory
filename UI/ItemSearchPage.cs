using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PelicanMemory.Core;
using PelicanMemory.Features.ItemSearch;
using PelicanMemory.Features.RecipeLookup;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>The search page of the game menu: type part of a name, pick an item, and see where to get it.</summary>
/// <remarks>
/// A search box with the matching items under it on the left, and what's known about the selected item on the right.
/// The box keeps the keyboard while the page is shown, so letters never trigger the game's keys; Escape still closes
/// the menu. Everything shown comes from <see cref="ItemSearchService"/>, which only knows what the player met.
/// </remarks>
internal class ItemSearchPage : IClickableMenu, IModMenuPage
{
    /*********
    ** Fields
    *********/
    /// <summary>The most matches listed, which is plenty once a few letters are typed.</summary>
    private const int MaxMatches = 200;

    private const int RowHeight = 56;
    private const int ListWidth = 320;
    private const int IconSize = 40;

    /// <summary>The most chests listed before the rest are summed up.</summary>
    private const int MaxChestLines = 6;

    private static readonly Color ReadyColor = new(40, 120, 60);
    private static readonly Color StoredColor = new(190, 120, 30);
    private static readonly Color MissingColor = new(170, 60, 40);
    private static readonly Color GridColor = new(120, 90, 60);

    private readonly ITranslationHelper Translations;
    private readonly ItemSearchService Search;
    private readonly IMonitor Monitor;
    private readonly TextBox SearchBox;

    /// <summary>Every name the search may find, read when the page is shown.</summary>
    private List<SearchEntry> Entries = new();

    /// <summary>The items matching the current text.</summary>
    private IReadOnlyList<SearchEntry> Matches = Array.Empty<SearchEntry>();

    /// <summary>How many of each listed item the player owns, worked out once per item.</summary>
    private readonly Dictionary<string, int> OwnedCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The text the matches were found for, to notice when it changes.</summary>
    private string LastQuery = "";

    private int SelectedIndex = -1;
    private int ListScroll;

    /// <summary>The lines describing the selected item, built when the selection changes.</summary>
    private List<DetailLine> Details = new();
    private int DetailScroll;

    private Rectangle ListArea => new(this.xPositionOnScreen + 48, this.yPositionOnScreen + 180, ListWidth, this.height - 180 - 48);
    private Rectangle DetailArea => new(this.xPositionOnScreen + 48 + ListWidth + 48, this.yPositionOnScreen + 108, this.width - 48 - ListWidth - 48 - 48, this.height - 108 - 48);
    private int VisibleRows => this.ListArea.Height / RowHeight;

    /// <summary>One line of the details panel.</summary>
    private record DetailLine(string Text, SpriteFont Font, float Scale, Color Color, int Indent, int Height, Texture2D? Icon = null, Rectangle IconSource = default, Color? IconTint = null);


    /*********
    ** Public methods
    *********/
    public ItemSearchPage(int x, int y, int width, int height, ITranslationHelper translations, ItemSearchService search, IMonitor monitor)
        : base(x, y, width, height)
    {
        this.Translations = translations;
        this.Search = search;
        this.Monitor = monitor;

        this.SearchBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites\\textBox"), null, Game1.smallFont, Game1.textColor)
        {
            X = this.xPositionOnScreen + 48,
            Y = this.yPositionOnScreen + 108,
            Width = ListWidth,
            textLimit = 40,
            TitleText = translations.Get("search.placeholder")
        };
    }

    /// <inheritdoc />
    public bool CapturesKeyboard => this.SearchBox.Selected && !Game1.options.gamepadControls;

    /// <inheritdoc />
    public void OnShown()
    {
        try
        {
            this.Search.Refresh();
            this.Entries = this.Search.GetEntries();
            this.OwnedCounts.Clear();
            this.LastQuery = null!; // find the matches again: what the player owns may have changed
            this.UpdateMatches();
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to prepare the item search:\n{ex}", LogLevel.Error);
        }

        // ready to type straight away
        if (!Game1.options.gamepadControls)
            this.SearchBox.Selected = true;
    }

    /// <inheritdoc />
    public void OnHidden()
    {
        this.SearchBox.Selected = false;
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        base.update(time);
        this.UpdateMatches();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        // the box: select it (with a controller, this opens the on-screen keyboard)
        Rectangle box = new(this.SearchBox.X, this.SearchBox.Y, this.SearchBox.Width, this.SearchBox.Height);
        if (box.Contains(x, y))
        {
            this.SearchBox.Update();
            return;
        }

        // a row: select its item, and keep the keyboard in the box so the player can refine the search
        Rectangle list = this.ListArea;
        if (list.Contains(x, y))
        {
            int index = this.ListScroll + (y - list.Y) / RowHeight;
            if (index < this.Matches.Count && index != this.SelectedIndex)
            {
                this.Select(index);
                Game1.playSound("smallSelect");
            }
        }
    }

    /// <inheritdoc />
    public override void receiveKeyPress(Keys key)
    {
        // while typing, letters belong to the box: only the arrows move through the list
        switch (key)
        {
            case Keys.Down when this.SelectedIndex < this.Matches.Count - 1:
                this.Select(this.SelectedIndex + 1);
                this.ScrollToSelection();
                Game1.playSound("shiny4");
                return;

            case Keys.Up when this.SelectedIndex > 0:
                this.Select(this.SelectedIndex - 1);
                this.ScrollToSelection();
                Game1.playSound("shiny4");
                return;
        }

        if (!this.CapturesKeyboard)
            base.receiveKeyPress(key);
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        int step = direction > 0 ? -1 : 1;

        if (this.DetailArea.Contains(Game1.getMouseX(), Game1.getMouseY()))
        {
            int maximum = Math.Max(0, this.Details.Count - 1);
            int scroll = Math.Clamp(this.DetailScroll + step, 0, maximum);
            if (scroll != this.DetailScroll && this.DetailsOverflow(step))
            {
                this.DetailScroll = scroll;
                Game1.playSound("shiny4");
            }
            return;
        }

        int listMaximum = Math.Max(0, this.Matches.Count - this.VisibleRows);
        int listScroll = Math.Clamp(this.ListScroll + step, 0, listMaximum);
        if (listScroll != this.ListScroll)
        {
            this.ListScroll = listScroll;
            Game1.playSound("shiny4");
        }
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        try
        {
            this.SearchBox.Draw(b);
            if (string.IsNullOrEmpty(this.SearchBox.Text))
                Utility.drawTextWithShadow(b, this.Translations.Get("search.placeholder"), Game1.smallFont, new Vector2(this.SearchBox.X + 16, this.SearchBox.Y + 12), Game1.textColor * 0.45f);

            // the line between the list and the details
            Rectangle details = this.DetailArea;
            b.Draw(Game1.staminaRect, new Rectangle(details.X - 24, details.Y, 2, details.Height), GridColor * 0.35f);

            this.DrawList(b);
            this.DrawDetails(b);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the item search:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Find the matches again if the text changed.</summary>
    private void UpdateMatches()
    {
        string query = this.SearchBox.Text ?? "";
        if (query == this.LastQuery)
            return;

        this.LastQuery = query;
        this.Matches = NameMatcher.Find(this.Entries, query, MaxMatches);
        this.ListScroll = 0;
        this.Select(this.Matches.Count > 0 ? 0 : -1);
    }

    /// <summary>Select a listed item and describe it.</summary>
    private void Select(int index)
    {
        this.SelectedIndex = index;
        this.DetailScroll = 0;
        this.Details = index >= 0 && index < this.Matches.Count
            ? this.BuildDetails(this.Search.GetDetails(this.Matches[index]))
            : new List<DetailLine>();
    }

    /// <summary>Scroll the list just enough to show the selected row.</summary>
    private void ScrollToSelection()
    {
        if (this.SelectedIndex < this.ListScroll)
            this.ListScroll = this.SelectedIndex;
        else if (this.SelectedIndex >= this.ListScroll + this.VisibleRows)
            this.ListScroll = this.SelectedIndex - this.VisibleRows + 1;
    }

    /// <summary>Get how many of an item the player owns, worked out once per item while the page is open.</summary>
    private int GetOwned(string itemId)
    {
        if (!this.OwnedCounts.TryGetValue(itemId, out int count))
            this.OwnedCounts[itemId] = count = this.Search.CountOwned(itemId);
        return count;
    }

    /// <summary>Draw the matching items, with how many the player owns of each.</summary>
    private void DrawList(SpriteBatch b)
    {
        Rectangle area = this.ListArea;

        if (this.Matches.Count == 0)
        {
            string message = string.IsNullOrWhiteSpace(this.SearchBox.Text)
                ? this.Translations.Get("search.help")
                : this.Translations.Get("search.no-match");
            string wrapped = Game1.parseText(message, Game1.smallFont, area.Width);
            Utility.drawTextWithShadow(b, wrapped, Game1.smallFont, new Vector2(area.X, area.Y), Game1.textColor * 0.6f);
            return;
        }

        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();

        for (int row = 0; row < this.VisibleRows; row++)
        {
            int index = this.ListScroll + row;
            if (index >= this.Matches.Count)
                break;

            SearchEntry entry = this.Matches[index];
            Rectangle bounds = new(area.X, area.Y + row * RowHeight, area.Width, RowHeight - 4);

            if (index == this.SelectedIndex)
                b.Draw(Game1.staminaRect, bounds, new Color(255, 205, 120) * 0.45f);
            else if (bounds.Contains(mouseX, mouseY))
                b.Draw(Game1.staminaRect, bounds, new Color(255, 205, 120) * 0.2f);

            DrawSprite(b, entry.Id, bounds.X + 6, bounds.Y + (bounds.Height - IconSize) / 2, IconSize);

            int owned = this.GetOwned(entry.Id);
            string count = owned > 0 ? owned.ToString() : "";
            float countWidth = Game1.smallFont.MeasureString(count).X * 0.8f;
            if (owned > 0)
                Utility.drawTextWithShadow(b, count, Game1.smallFont, new Vector2(bounds.Right - 8 - countWidth, bounds.Y + 14), Game1.textColor * 0.6f, 0.8f);

            int nameX = bounds.X + IconSize + 16;
            DrawTruncated(b, entry.Name, Game1.smallFont, nameX, bounds.Y + 12, (int)(bounds.Right - 16 - countWidth - nameX), Game1.textColor, 0.95f);
        }

        // more below or above: say so, the list scrolls with the wheel
        if (this.Matches.Count > this.VisibleRows)
        {
            string hint = this.Translations.Get("search.scroll", new { shown = Math.Min(this.ListScroll + this.VisibleRows, this.Matches.Count), total = this.Matches.Count });
            Utility.drawTextWithShadow(b, hint, Game1.smallFont, new Vector2(area.X, area.Bottom + 4), Game1.textColor * 0.5f, 0.75f);
        }
    }

    /// <summary>Draw what's known about the selected item, from the scroll position.</summary>
    private void DrawDetails(SpriteBatch b)
    {
        Rectangle area = this.DetailArea;
        int y = area.Y;

        foreach (DetailLine line in this.Details.Skip(this.DetailScroll))
        {
            if (y + line.Height > area.Bottom)
            {
                Utility.drawTextWithShadow(b, this.Translations.Get("search.details-more"), Game1.smallFont, new Vector2(area.X, area.Bottom - 4), Game1.textColor * 0.5f, 0.75f);
                break;
            }

            int x = area.X + line.Indent;
            if (line.Icon != null)
            {
                int size = line.Height >= 56 ? 48 : 24;
                DrawFitted(b, line.Icon, line.IconSource, x, y + (line.Height - size) / 2 - 2, size, line.IconTint ?? Color.White);
                x += size + 10;
            }

            if (line.Text.Length > 0)
            {
                float textHeight = line.Font.MeasureString(line.Text).Y * line.Scale;
                Utility.drawTextWithShadow(b, line.Text, line.Font, new Vector2(x, y + (line.Height - textHeight) / 2), line.Color, line.Scale);
            }

            y += line.Height;
        }
    }

    /// <summary>Get whether scrolling the details in a direction would reveal more.</summary>
    private bool DetailsOverflow(int step)
    {
        if (step < 0)
            return this.DetailScroll > 0;

        int height = this.Details.Skip(this.DetailScroll).Sum(line => line.Height);
        return height > this.DetailArea.Height;
    }

    /// <summary>Turn what's known about an item into lines: owned, stored, sold, made.</summary>
    private List<DetailLine> BuildDetails(ItemDetails details)
    {
        List<DetailLine> lines = new();
        int width = this.DetailArea.Width;
        SpriteFont small = Game1.smallFont;
        Color muted = Game1.textColor * 0.6f;

        void Text(string text, Color color, int indent = 0, float scale = 0.9f, Texture2D? icon = null, Rectangle iconSource = default, Color? tint = null)
        {
            int textWidth = width - indent - (icon != null ? 34 : 0);
            string[] wrapped = Game1.parseText(text, small, (int)(textWidth / scale)).Split('\n');
            for (int i = 0; i < wrapped.Length; i++)
                lines.Add(new DetailLine(wrapped[i], small, scale, color, indent, 34, i == 0 ? icon : null, iconSource, tint));
        }

        void Heading(string text)
        {
            lines.Add(new DetailLine("", small, 1f, Color.Transparent, 0, 12));
            Text(text, Game1.textColor, scale: 0.95f);
        }

        // the item itself
        // the item itself, its name as large as fits beside its sprite
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(details.ItemId);
        int nameWidth = width - 58;
        float titleScale = new[] { 0.8f, 0.7f, 0.6f }.FirstOrDefault(scale => Game1.dialogueFont.MeasureString(details.Name).X * scale <= nameWidth);
        if (titleScale > 0)
            lines.Add(new DetailLine(details.Name, Game1.dialogueFont, titleScale, Game1.textColor, 0, 64, data.GetTexture(), data.GetSourceRect()));
        else
        {
            lines.Add(new DetailLine("", small, 1f, Color.Transparent, 0, 64, data.GetTexture(), data.GetSourceRect()));
            Text(details.Name, Game1.textColor, scale: 1f);
        }

        // what the player has
        Text(this.Translations.Get("search.on-you", new { count = details.InBag }), details.InBag > 0 ? ReadyColor : muted);
        if (details.Stored > 0)
        {
            Text(this.Translations.Get("search.stored", new { count = details.Stored }), StoredColor);
            foreach (ItemStash stash in details.Stashes.Take(MaxChestLines))
            {
                ParsedItemData chest = ItemRegistry.GetDataOrErrorItem(stash.ChestId);
                Text($"{stash.Count} — {stash.DisplayName}", Game1.textColor, indent: 24, scale: 0.85f, icon: chest.GetTexture(), iconSource: chest.GetSourceRect(), tint: stash.ChestColor);
            }
            if (details.Stashes.Count > MaxChestLines)
                Text(this.Translations.Get("search.more-chests", new { count = details.Stashes.Count - MaxChestLines }), muted, indent: 24, scale: 0.85f);
        }
        else
            Text(this.Translations.Get("search.stored-none"), muted);

        // where to buy it today
        if (details.Offers.Count > 0)
        {
            Heading(this.Translations.Get("search.shops"));
            foreach (ShopOffer offer in details.Offers)
                Text($"{this.Translations.Get($"shop.{offer.ShopId}")} — {this.FormatPrice(offer)}", Game1.textColor, indent: 24, scale: 0.85f);
        }

        // how to make it
        foreach (bool cooking in new[] { false, true })
        {
            List<KnownRecipe> recipes = details.Recipes.Where(recipe => recipe.IsCooking == cooking).ToList();
            if (recipes.Count == 0)
                continue;

            Heading(this.Translations.Get(cooking ? "search.cooking" : "search.crafting"));
            foreach (KnownRecipe recipe in recipes)
            {
                Color status = recipe.CanMakeNow > 0 ? ReadyColor : recipe.CanMakeWithChests > 0 ? StoredColor : MissingColor;
                if (recipes.Count > 1 || recipe.Name != details.Name)
                    Text(recipe.Name, status, indent: 24, scale: 0.85f);

                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    int have = ingredient.InBag + ingredient.InChests;
                    Color color = ingredient.InBag >= ingredient.Needed ? ReadyColor : have >= ingredient.Needed ? StoredColor : MissingColor;
                    ParsedItemData sprite = ItemRegistry.GetDataOrErrorItem(ingredient.SpriteId);
                    Text(
                        this.Translations.Get("search.ingredient", new { need = ingredient.Needed, name = ingredient.Name, have }),
                        color, indent: 40, scale: 0.85f, icon: sprite.GetTexture(), iconSource: sprite.GetSourceRect()
                    );
                }
            }
        }

        // nothing at all
        if (details.IsUnknownSource)
        {
            lines.Add(new DetailLine("", small, 1f, Color.Transparent, 0, 12));
            Text(this.Translations.Get("search.nothing"), muted);
        }

        // a farmhand's game doesn't receive the host's cellar or island: say so rather than look like they're empty
        if (!Context.IsMainPlayer)
        {
            lines.Add(new DetailLine("", small, 1f, Color.Transparent, 0, 12));
            Text(this.Translations.Get("search.farmhand-note"), muted, scale: 0.75f);
        }

        return lines;
    }

    /// <summary>Write a price in the shop's currency, or the items it asks in exchange.</summary>
    private string FormatPrice(ShopOffer offer)
    {
        List<string> parts = new();

        if (offer.Price > 0)
        {
            string key = offer.Currency switch
            {
                1 => "price.star-tokens",
                2 => "price.qi-coins",
                4 => "price.qi-gems",
                _ => "price.gold"
            };
            parts.Add(this.Translations.Get(key, new { price = offer.Price }));
        }

        if (offer.TradeItemId != null)
            parts.Add(this.Translations.Get("price.trade", new { count = offer.TradeCount, item = ItemRegistry.GetDataOrErrorItem(offer.TradeItemId).DisplayName }));

        return parts.Count > 0 ? string.Join(" + ", parts) : this.Translations.Get("price.free");
    }

    /// <summary>Draw an item sprite fitted into a square.</summary>
    private static void DrawSprite(SpriteBatch b, string itemId, int x, int y, int size)
    {
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(itemId);
        DrawFitted(b, data.GetTexture(), data.GetSourceRect(), x, y, size, Color.White);
    }

    /// <summary>Draw part of a texture fitted and centred into a square.</summary>
    private static void DrawFitted(SpriteBatch b, Texture2D texture, Rectangle source, int x, int y, int size, Color tint)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return;

        float scale = size / (float)Math.Max(source.Width, source.Height);
        Vector2 position = new(x + (size - source.Width * scale) / 2f, y + (size - source.Height * scale) / 2f);
        b.Draw(texture, position, source, tint, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.88f);
    }

    /// <summary>Draw text, shortened if it would run past a width.</summary>
    private static void DrawTruncated(SpriteBatch b, string text, SpriteFont font, int x, int y, int maxWidth, Color color, float scale)
    {
        if (font.MeasureString(text).X * scale > maxWidth)
        {
            // plain dots: the game's fonts don't all have an ellipsis character
            while (text.Length > 1 && font.MeasureString(text + "...").X * scale > maxWidth)
                text = text[..^1];
            text = text.TrimEnd() + "...";
        }

        Utility.drawTextWithShadow(b, text, font, new Vector2(x, y), color, scale);
    }
}
