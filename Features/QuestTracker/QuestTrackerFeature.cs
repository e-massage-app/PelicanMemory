using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;
using StardewValley.SpecialOrders;

namespace PelicanMemory.Features.QuestTracker;

/// <summary>Keeps the quests with a deadline in sight: a small strip at the top of the screen shows one of them (what's
/// left to do, the count, the days left), with arrows to go through the others; the eye hides it, a click on the little
/// tab left in its place shows it again, and a click on the quest opens it in the journal.</summary>
/// <remarks>
/// The game only shows them behind the "!" of the journal, so a request taken on the board is easily forgotten until it
/// has expired. The panel sits at the top of the screen, just under the toolbar when the toolbar is at the top.
///
/// Anti-spoil: only the quests already in the player's journal (taken by the player), as the journal shows them.
/// </remarks>
internal class QuestTrackerFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The strip's size, in pixels.</summary>
    private const int Width = 620;
    private const int Height = 80;

    /// <summary>The config key: the strip is hidden (only the little tab to show it again is left).</summary>
    private const string HiddenKey = "quest-tracker.hidden";

    /// <summary>The vanilla left and right arrows in <see cref="Game1.mouseCursors"/>, drawn three times bigger.</summary>
    private static readonly Rectangle LeftArrow = new(352, 495, 12, 11);
    private static readonly Rectangle RightArrow = new(365, 495, 12, 11);

    /// <summary>The colours: the game's dark text, the eye's white and pupil.</summary>
    private static readonly Color Ink = new(86, 22, 12);
    private static readonly Color EyeWhite = new(255, 250, 235);
    private static readonly Color Iris = new(80, 130, 210);
    private static readonly Color IrisShade = new(50, 90, 170);
    private static readonly Color Pupil = new(25, 25, 50);

    /// <summary>A small eye, drawn pixel by pixel (15 × 9, three times bigger): '#' outline, '.' white, 'o'/'O' iris,
    /// '@' pupil, 'w' highlight.</summary>
    private static readonly string[] Eye =
    {
        "     #####     ",
        "   ##ooooo##   ",
        "  #.oO@@@Oo.#  ",
        " #..o@@w@@o..# ",
        "#...o@@@@@o...#",
        " #..oO@@@Oo..# ",
        "  #..ooooo..#  ",
        "   ##.....##   ",
        "     #####     ",
    };
    private const int EyeWidth = 45;
    private const int EyeHeight = 27;

    /// <summary>The quest shown (the others are one arrow away).</summary>
    private IQuest? Selected;

    /// <summary>The clickable parts on screen, updated each time they're drawn.</summary>
    private Rectangle PrevButton, NextButton, EyeButton, QuestArea, ShowTab;

    /// <summary>The quest drawn last, for a click on it.</summary>
    private IQuest? Drawn;


    /*********
    ** Public methods
    *********/
    public override string Id => "quest-tracker";

    /// <summary>The active instance, for the automated tests.</summary>
    internal static QuestTrackerFeature? Active { get; private set; }

    /// <summary>For the automated tests: open a quest in the journal as a click on the strip does.</summary>
    internal static void Open(IQuest quest) => OpenInJournal(quest);

    /// <summary>For the automated tests: the quest on show, and the clickable parts.</summary>
    internal IQuest? Shown => this.Drawn;
    internal Rectangle[] Buttons => new[] { this.PrevButton, this.NextButton, this.EyeButton, this.QuestArea, this.ShowTab };

    public QuestTrackerFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }

    /// <summary>The quests to go through, most urgent first: only those with a deadline, still to do (none: nothing shows).</summary>
    public IEnumerable<IQuest> GetQuests()
    {
        IEnumerable<IQuest> quests = Game1.player.team.specialOrders
            .Where(o => !o.IsHidden() && o.questState.Value == SpecialOrderStatus.InProgress)
            .Cast<IQuest>()
            .Concat(Game1.player.questLog.Where(q => !q.IsHidden() && !q.completed.Value && !q.destroy.Value && q.IsTimedQuest()));
        return quests
            .Where(q => q.GetDaysLeft() > 0)
            .OrderBy(q => q.GetDaysLeft())
            .ToList();
    }

    /// <summary>For the automated tests and the click handler: show the next (or previous) quest.</summary>
    internal void Step(int direction)
    {
        List<IQuest> quests = this.GetQuests().ToList();
        if (quests.Count == 0)
            return;
        int index = Math.Max(0, this.Selected != null ? quests.IndexOf(this.Selected) : 0);
        this.Selected = quests[(index + direction % quests.Count + quests.Count) % quests.Count];
    }

    /// <summary>For the automated tests and the click handler: hide the strip, or show it again.</summary>
    internal void SetHidden(bool hidden) => this.Settings.SetFlag(HiddenKey, hidden);


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Active = this;
        this.Helper.Events.Display.RenderedHud += this.OnRenderedHud;
        this.Helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    protected override void OnDisable()
    {
        this.Helper.Events.Display.RenderedHud -= this.OnRenderedHud;
        this.Helper.Events.Input.ButtonPressed -= this.OnButtonPressed;
        this.Selected = this.Drawn = null;
        Active = null;
    }


    /*********
    ** Private methods
    *********/
    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        this.PrevButton = this.NextButton = this.EyeButton = this.QuestArea = this.ShowTab = Rectangle.Empty;
        this.Drawn = null;
        if (!ShouldDraw())
            return;

        try
        {
            List<IQuest> quests = this.GetQuests().ToList();
            if (quests.Count == 0)
                return;
            if (this.Settings.GetFlag(HiddenKey, false))
                this.DrawTab(e.SpriteBatch, quests.Count);
            else
                this.DrawStrip(e.SpriteBatch, quests);
        }
        catch (Exception ex)
        {
            this.Monitor.LogOnce($"Failed to draw the quest tracker:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>One quest: name and days left on the first line, what's left to do and the count on the second.</summary>
    private void DrawStrip(SpriteBatch b, List<IQuest> quests)
    {
        int index = Math.Max(0, this.Selected != null ? quests.IndexOf(this.Selected) : 0);
        IQuest quest = quests[index];
        this.Selected = this.Drawn = quest;

        int x = Game1.uiViewport.Width / 2 - Width / 2;
        int y = Top();
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, Width, Height, Color.White, 1f, drawShadow: true);

        // arrows, only when there's more than one quest
        bool several = quests.Count > 1;
        this.PrevButton = several ? new Rectangle(x + 14, y + Height / 2 - 16, 36, 33) : Rectangle.Empty;
        this.NextButton = several ? new Rectangle(x + Width - 106, y + Height / 2 - 16, 36, 33) : Rectangle.Empty;
        if (several)
        {
            b.Draw(Game1.mouseCursors, this.PrevButton, LeftArrow, Color.White);
            b.Draw(Game1.mouseCursors, this.NextButton, RightArrow, Color.White);
        }

        // the eye (hide), with "2/3" under it
        this.EyeButton = new Rectangle(x + Width - 62, y + 14, EyeWidth, EyeHeight);
        DrawEye(b, this.EyeButton.Location);
        if (several)
        {
            string count = $"{index + 1}/{quests.Count}";
            Vector2 size = Game1.smallFont.MeasureString(count) * 0.7f;
            b.DrawString(Game1.smallFont, count, new Vector2(this.EyeButton.Center.X - size.X / 2, y + 46), Ink, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 1f);
        }

        // the quest itself
        int textLeft = x + (several ? 58 : 22);
        int textRight = x + Width - (several ? 114 : 72);
        this.QuestArea = new Rectangle(textLeft - 4, y + 8, textRight - textLeft + 8, Height - 16);
        SpriteFont font = Game1.smallFont;

        string? days = null;
        Color daysColor = Ink;
        if (quest.IsTimedQuest() && quest.GetDaysLeft() > 0)
        {
            int left = quest.GetDaysLeft();
            days = left <= 1 ? this.Helper.Translation.Get("quest-tracker.last-day") : this.Helper.Translation.Get("quest-tracker.days-left", new { count = left });
            daysColor = left <= 1 ? new Color(200, 20, 20) : left == 2 ? new Color(190, 80, 0) : Ink;
        }
        int daysWidth = days != null ? (int)font.MeasureString(days).X : 0;
        if (days != null)
            b.DrawString(font, days, new Vector2(textRight - daysWidth, y + 14), daysColor);
        b.DrawString(font, Fit(quest.GetName(), font, textRight - textLeft - daysWidth - 12), new Vector2(textLeft, y + 14), Ink);

        (string text, int done, int total) = GetObjective(quest);
        if (total > 0 && text.Contains($"{done}/{total}"))
            total = 0;                                     // the game's own line already says "0/5 …"
        string progress = total > 0 ? $"{done}/{total}" : "";
        int progressWidth = total > 0 ? (int)font.MeasureString(progress).X : 0;
        if (total > 0)
            b.DrawString(font, progress, new Vector2(textRight - progressWidth, y + 42), done >= total ? new Color(40, 120, 20) : Ink);
        b.DrawString(font, Fit(text, font, textRight - textLeft - progressWidth - 12), new Vector2(textLeft, y + 42), Ink * 0.85f);
    }

    /// <summary>Hidden: a little tab with the eye and the number of quests, to show the strip again.</summary>
    private void DrawTab(SpriteBatch b, int count)
    {
        string label = this.Helper.Translation.Get("quest-tracker.show", new { count });
        int width = 20 + EyeWidth + 10 + (int)Game1.smallFont.MeasureString(label).X + 20;
        int x = Game1.uiViewport.Width / 2 - width / 2;
        int y = Top();
        this.ShowTab = new Rectangle(x, y, width, 52);
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), x, y, width, 52, Color.White, 1f, drawShadow: true);
        DrawEye(b, new Point(x + 20, y + 12));
        b.DrawString(Game1.smallFont, label, new Vector2(x + 20 + EyeWidth + 10, y + 12), Ink);
    }

    /// <summary>Draw the little eye at a screen position.</summary>
    private static void DrawEye(SpriteBatch b, Point at)
    {
        for (int row = 0; row < Eye.Length; row++)
        {
            for (int col = 0; col < Eye[row].Length; col++)
            {
                Color? color = Eye[row][col] switch { '#' => Ink, '.' => EyeWhite, 'o' => Iris, 'O' => IrisShade, '@' => Pupil, 'w' => Color.White, _ => null };
                if (color is Color c)
                    b.Draw(Game1.staminaRect, new Rectangle(at.X + col * 3, at.Y + row * 3, 3, 3), c);
            }
        }
    }

    /// <summary>The first objective still to do, with its progress when the quest counts something.</summary>
    private static (string Text, int Done, int Total) GetObjective(IQuest quest)
    {
        switch (quest)
        {
            case SpecialOrder order:
                // the translated lines, as the journal shows them (the objectives keep their raw "[key]" texts)
                List<string> lines = order.GetObjectiveDescriptions();
                for (int i = 0; i < order.objectives.Count; i++)
                {
                    var objective = order.objectives[i];
                    if (!objective.IsComplete())
                        return (i < lines.Count ? lines[i] : objective.GetDescription(), objective.GetCount(), objective.GetMaxCount());
                }
                return (lines.FirstOrDefault() ?? "", 0, 0);
            case SlayMonsterQuest slay:
                return (slay.currentObjective, slay.numberKilled.Value, slay.numberToKill.Value);
            case FishingQuest fishing:
                return (fishing.currentObjective, fishing.numberFished.Value, fishing.numberToFish.Value);
            case ResourceCollectionQuest collect:
                return (collect.currentObjective, collect.numberCollected.Value, collect.number.Value);
            case Quest q:
                return (q.currentObjective, 0, 0);
            default:
                return (quest.GetObjectiveDescriptions().FirstOrDefault() ?? "", 0, 0);
        }
    }

    /// <summary>Shorten a text with an ellipsis until it fits.</summary>
    private static string Fit(string? text, SpriteFont font, int width)
    {
        text = (text ?? "").Replace('\n', ' ');
        if (font.MeasureString(text).X <= width)
            return text;
        while (text.Length > 1 && font.MeasureString(text + "…").X > width)
            text = text[..^1];
        return text.TrimEnd() + "…";
    }

    /// <summary>The top of the panel: just under the toolbar when the toolbar is at the top of the screen, otherwise the top.</summary>
    private static int Top()
    {
        Toolbar? toolbar = Game1.onScreenMenus.OfType<Toolbar>().FirstOrDefault();
        if (toolbar != null && Game1.displayHUD && toolbar.yPositionOnScreen < Game1.uiViewport.Height / 2)
            return toolbar.yPositionOnScreen + 4;      // the toolbar's frame ends at yPositionOnScreen - 8
        return Utility.makeSafeMarginY(8);
    }

    /// <summary>Get whether the panel should be visible right now.</summary>
    private static bool ShouldDraw()
    {
        return Context.IsWorldReady
            && Game1.displayHUD
            && Game1.activeClickableMenu is null
            && Game1.currentMinigame is null
            && !Game1.eventUp
            && Game1.farmEvent is null
            && !Game1.game1.takingMapScreenshot;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.Button != SButton.MouseLeft || !Context.IsPlayerFree || !ShouldDraw())
            return;
        Vector2 cursor = e.Cursor.GetScaledScreenPixels();
        Point point = new((int)cursor.X, (int)cursor.Y);

        if (this.EyeButton.Contains(point) || this.ShowTab.Contains(point))
        {
            this.SetHidden(this.EyeButton.Contains(point));
            Game1.playSound("shwip");
        }
        else if (this.PrevButton.Contains(point) || this.NextButton.Contains(point))
        {
            this.Step(this.NextButton.Contains(point) ? 1 : -1);
            Game1.playSound("shiny4");
        }
        else if (this.QuestArea.Contains(point) && this.Drawn != null)
        {
            Game1.playSound("bigSelect");
            OpenInJournal(this.Drawn);
        }
        else
            return;
        this.Helper.Input.Suppress(e.Button);
    }

    /// <summary>Open the journal on this quest's page, as if the player had clicked it there.</summary>
    private static void OpenInJournal(IQuest quest)
    {
        QuestLog log = new();
        Game1.activeClickableMenu = log;
        var pages = Traverse.Create(log).Field<List<List<IQuest>>>("pages").Value;
        for (int page = 0; page < pages.Count; page++)
        {
            int index = pages[page].IndexOf(quest);
            if (index < 0)
                continue;
            Traverse.Create(log).Field<int>("currentPage").Value = page;
            Point center = log.questLogButtons[index].bounds.Center;
            log.receiveLeftClick(center.X, center.Y, playSound: false);
            return;
        }
    }
}
