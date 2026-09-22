using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>Announces a new version on the title screen, lists everything it adds, and installs it in one click.</summary>
internal class UpdateMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    private const int MenuWidth = 860;
    private const int MenuHeight = 600;
    private const int ButtonHeight = 64;

    /// <summary>The space kept under the change list for the page arrows, when there's more than one page.</summary>
    private const int PagerHeight = 52;

    /// <summary>The gap between two changes in the list.</summary>
    private const int ChangeSpacing = 8;

    /// <summary>How long the "installed" message stays before the game closes, in milliseconds.</summary>
    private const int QuitDelay = 2000;

    /// <summary>The vanilla button frame, as used by the options page.</summary>
    private static readonly Rectangle ButtonSource = new(432, 439, 9, 9);

    /// <summary>The vanilla page arrows, as used by the collections page.</summary>
    private static readonly Rectangle BackArrow = new(352, 495, 12, 11);
    private static readonly Rectangle ForwardArrow = new(365, 495, 12, 11);

    private static readonly Color VersionColor = new(40, 100, 60);
    private static readonly Color ErrorColor = new(170, 60, 40);

    private enum State { Offer, Installing, Installed, Failed }

    /// <summary>One entry of the change list: a version heading or a change, already wrapped to the list's width.</summary>
    private record Block(string Text, bool IsHeading, int Height);

    private readonly ITranslationHelper Translations;
    private readonly IMonitor Monitor;
    private readonly SelfUpdater Updater;
    private readonly IReadOnlyList<ChangelogEntry> Versions;
    private readonly string CurrentVersion;
    private readonly bool IsFrench;

    // written by the download task, read by the draw loop
    private volatile State Current = State.Offer;
    private string? FailureReason;
    private bool WillRestart;
    private int QuitTimer;

    private Rectangle UpdateButton;
    private Rectangle LaterButton;
    private Rectangle ListArea;
    private ClickableTextureComponent BackButton = null!;
    private ClickableTextureComponent ForwardButton = null!;

    /// <summary>The change list split into pages that fit the window.</summary>
    private List<List<Block>> Pages = new();
    private int Page;


    /*********
    ** Public methods
    *********/
    public UpdateMenu(ITranslationHelper translations, IMonitor monitor, SelfUpdater updater, IReadOnlyList<ChangelogEntry> versions, string currentVersion)
        : base(Game1.uiViewport.Width / 2 - MenuWidth / 2, Game1.uiViewport.Height / 2 - MenuHeight / 2, MenuWidth, MenuHeight)
    {
        this.Translations = translations;
        this.Monitor = monitor;
        this.Updater = updater;
        this.Versions = versions;
        this.CurrentVersion = currentVersion;
        this.IsFrench = LocalizedContentManager.CurrentLanguageCode == LocalizedContentManager.LanguageCode.fr;
        this.LayOut();
    }

    /// <inheritdoc />
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        this.xPositionOnScreen = Game1.uiViewport.Width / 2 - MenuWidth / 2;
        this.yPositionOnScreen = Game1.uiViewport.Height / 2 - MenuHeight / 2;
        this.LayOut();
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        switch (this.Current)
        {
            case State.Offer when this.UpdateButton.Contains(x, y):
                this.StartInstall();
                break;

            case State.Offer when this.LaterButton.Contains(x, y):
            case State.Failed when this.LaterButton.Contains(x, y):
                Game1.playSound("bigDeSelect");
                TitleMenu.ReturnToMainTitleScreen();
                break;

            case State.Offer when this.HasPrevious && this.BackButton.containsPoint(x, y):
                this.TurnPage(-1);
                break;

            case State.Offer when this.HasNext && this.ForwardButton.containsPoint(x, y):
                this.TurnPage(1);
                break;
        }
    }

    /// <inheritdoc />
    public override void receiveScrollWheelAction(int direction)
    {
        if (this.Current == State.Offer)
            this.TurnPage(direction > 0 ? -1 : 1);
    }

    /// <inheritdoc />
    public override void receiveKeyPress(Keys key)
    {
        // escape means "not now", except while files are being replaced
        if (key == Keys.Escape && this.Current is State.Offer or State.Failed)
            TitleMenu.ReturnToMainTitleScreen();
        else if (this.Current == State.Offer && key is Keys.Left or Keys.Right)
            this.TurnPage(key == Keys.Left ? -1 : 1);
    }

    /// <inheritdoc />
    public override void performHoverAction(int x, int y)
    {
        this.BackButton.tryHover(x, y);
        this.ForwardButton.tryHover(x, y);
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        if (this.Current != State.Installed)
            return;

        this.QuitTimer -= time.ElapsedGameTime.Milliseconds;
        if (this.QuitTimer <= 0)
        {
            // exactly what the title screen's own "Exit" button does
            Game1.quit = true;
            Game1.exitActiveMenu();
        }
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.6f);
        drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        SpriteText.drawStringHorizontallyCenteredAt(b, this.Translations.Get("update.title"), this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen + 32);

        string versions = this.Translations.Get("update.versions", new { latest = this.Versions[0].Version, current = this.CurrentVersion });
        Utility.drawTextWithShadow(b, versions, Game1.smallFont, new Vector2(this.ListArea.X, this.yPositionOnScreen + 104), VersionColor);

        switch (this.Current)
        {
            case State.Offer:
                this.DrawPage(b);
                this.DrawButton(b, this.UpdateButton, this.Translations.Get("update.install"));
                this.DrawButton(b, this.LaterButton, this.Translations.Get("update.later"));
                break;

            case State.Installing:
                this.DrawMessage(b, this.Translations.Get("update.installing"), Game1.textColor);
                break;

            case State.Installed:
                this.DrawMessage(b, this.Translations.Get(this.WillRestart ? "update.installed-restart" : "update.installed"), VersionColor);
                break;

            case State.Failed:
                this.DrawMessage(b, this.Translations.Get("update.failed", new { reason = this.FailureReason }), ErrorColor);
                this.DrawButton(b, this.LaterButton, this.Translations.Get("update.close"));
                break;
        }

        this.drawMouse(b);
    }


    /*********
    ** Private methods
    *********/
    private bool HasPrevious => this.Page > 0;
    private bool HasNext => this.Page < this.Pages.Count - 1;

    /// <summary>Place the buttons and the list, and split the changes into pages that fit.</summary>
    private void LayOut()
    {
        int gap = 24;
        int buttonWidth = (this.width - 80 - gap) / 2;
        int buttonTop = this.yPositionOnScreen + this.height - ButtonHeight - 36;

        this.UpdateButton = new Rectangle(this.xPositionOnScreen + 40, buttonTop, buttonWidth, ButtonHeight);
        this.LaterButton = new Rectangle(this.UpdateButton.Right + gap, buttonTop, buttonWidth, ButtonHeight);

        int listTop = this.yPositionOnScreen + 152;
        this.ListArea = new Rectangle(this.xPositionOnScreen + 40, listTop, this.width - 80, buttonTop - 24 - listTop);

        int arrowY = this.ListArea.Bottom - PagerHeight + 4;
        this.BackButton = new ClickableTextureComponent(new Rectangle(this.ListArea.X, arrowY, 48, 44), Game1.mouseCursors, BackArrow, 4f);
        this.ForwardButton = new ClickableTextureComponent(new Rectangle(this.ListArea.Right - 48, arrowY, 48, 44), Game1.mouseCursors, ForwardArrow, 4f);

        this.Pages = this.Paginate(this.BuildBlocks());
        this.Page = Math.Min(this.Page, this.Pages.Count - 1);
    }

    /// <summary>Turn the list in the given direction, if there's a page there.</summary>
    private void TurnPage(int direction)
    {
        int page = Math.Clamp(this.Page + direction, 0, this.Pages.Count - 1);
        if (page == this.Page)
            return;

        this.Page = page;
        Game1.playSound("shwip");
    }

    /// <summary>Turn every change into a block of wrapped text, with a heading per version when there are several.</summary>
    private List<Block> BuildBlocks()
    {
        List<Block> blocks = new();
        bool severalVersions = this.Versions.Count > 1;

        foreach (ChangelogEntry entry in this.Versions)
        {
            if (severalVersions)
                blocks.Add(new Block(entry.Version, IsHeading: true, (int)Game1.smallFont.MeasureString(entry.Version).Y + 4));

            foreach (string change in (this.IsFrench ? entry.Fr : entry.En) ?? Array.Empty<string>())
            {
                string wrapped = Game1.parseText("• " + change, Game1.smallFont, this.ListArea.Width);
                blocks.Add(new Block(wrapped, IsHeading: false, (int)Game1.smallFont.MeasureString(wrapped).Y + ChangeSpacing));
            }
        }

        return blocks;
    }

    /// <summary>Split the blocks into pages, leaving room for the arrows only when there's more than one page.</summary>
    private List<List<Block>> Paginate(List<Block> blocks)
    {
        List<List<Block>> pages = Split(blocks, this.ListArea.Height);
        return pages.Count > 1
            ? Split(blocks, this.ListArea.Height - PagerHeight)
            : pages;

        static List<List<Block>> Split(List<Block> blocks, int height)
        {
            List<List<Block>> pages = new() { new List<Block>() };
            int used = 0;

            foreach (Block block in blocks)
            {
                // a heading never ends a page on its own: it moves on with the change that follows it
                bool full = used + block.Height > height;
                bool lastIsHeading = pages[^1].Count > 0 && pages[^1][^1].IsHeading;

                if (full && pages[^1].Count > 0)
                {
                    List<Block> next = new();
                    if (lastIsHeading)
                    {
                        next.Add(pages[^1][^1]);
                        pages[^1].RemoveAt(pages[^1].Count - 1);
                    }

                    pages.Add(next);
                    used = next.Sum(entry => entry.Height);
                }

                pages[^1].Add(block);
                used += block.Height;
            }

            return pages;
        }
    }

    /// <summary>Draw the current page of changes, and the arrows when there are several pages.</summary>
    private void DrawPage(SpriteBatch b)
    {
        int y = this.ListArea.Y;
        foreach (Block block in this.Pages[this.Page])
        {
            Color color = block.IsHeading ? Game1.textColor * 0.6f : Game1.textColor;
            Utility.drawTextWithShadow(b, block.Text, Game1.smallFont, new Vector2(this.ListArea.X, y), color);
            y += block.Height;
        }

        if (this.Pages.Count <= 1)
            return;

        if (this.HasPrevious)
            this.BackButton.draw(b);
        if (this.HasNext)
            this.ForwardButton.draw(b);

        string counter = this.Translations.Get("update.page", new { page = this.Page + 1, total = this.Pages.Count });
        Vector2 size = Game1.smallFont.MeasureString(counter);
        Utility.drawTextWithShadow(b, counter, Game1.smallFont, new Vector2(this.ListArea.Center.X - size.X / 2, this.BackButton.bounds.Center.Y - size.Y / 2), Game1.textColor * 0.6f);
    }

    /// <summary>Download and install the newest version, then restart the game once it's in place.</summary>
    private void StartInstall()
    {
        Game1.playSound("bigSelect");
        this.Current = State.Installing;

        string version = this.Versions[0].Version;
        Task.Run(async () =>
        {
            try
            {
                await this.Updater.InstallAsync(version);
                this.Monitor.Log($"Installed version {version}; it will load on the next launch.", LogLevel.Info);

                this.WillRestart = GameRestarter.TrySchedule(this.Monitor);
                this.QuitTimer = QuitDelay;
                this.Current = State.Installed;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't install version {version}. Nothing was changed.\n{ex}", LogLevel.Error);
                this.FailureReason = ex is System.Net.Http.HttpRequestException or TaskCanceledException
                    ? this.Translations.Get("update.reason-network")
                    : ex.Message;
                this.Current = State.Failed;
            }
        });
    }

    /// <summary>Draw a centred message in the space the change list normally uses.</summary>
    private void DrawMessage(SpriteBatch b, string message, Color color)
    {
        string wrapped = Game1.parseText(message, Game1.smallFont, this.width - 120);
        Vector2 size = Game1.smallFont.MeasureString(wrapped);
        Vector2 position = new(this.xPositionOnScreen + (this.width - size.X) / 2, this.yPositionOnScreen + (this.height - size.Y) / 2);
        Utility.drawTextWithShadow(b, wrapped, Game1.smallFont, position, color);
    }

    /// <summary>Draw a vanilla-style button, lighter while hovered, with its label shrunk if it wouldn't fit.</summary>
    private void DrawButton(SpriteBatch b, Rectangle area, string label)
    {
        bool hovered = area.Contains(Game1.getOldMouseX(), Game1.getOldMouseY());
        drawTextureBox(b, Game1.mouseCursors, ButtonSource, area.X, area.Y, area.Width, area.Height, hovered ? Color.Wheat : Color.White, 4f, drawShadow: false);

        Vector2 size = Game1.smallFont.MeasureString(label);
        float scale = Math.Min(1f, (area.Width - 40) / size.X);
        Vector2 position = new(area.X + (area.Width - size.X * scale) / 2, area.Y + (area.Height - size.Y * scale) / 2 + 2);
        Utility.drawTextWithShadow(b, label, Game1.smallFont, position, Game1.textColor, scale);
    }
}
