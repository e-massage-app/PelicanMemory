using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace PelicanMemory.Features.LuckIndicator;

/// <summary>Shows the day's luck on the clock, like the weather: the spirit the TV's fortune teller would show.</summary>
/// <remarks>
/// The game only tells the day's luck through the fortune teller on the TV. The clock already has a little slot between
/// the weather and the season (only a small brass plate is drawn there): the spirit's face goes there, still, with the same five levels and
/// thresholds (<c>TV.setFortuneOverlay</c>). Hovering it shows what the fortune teller says that day
/// (<c>TV.getFortuneForecast</c>, worked out once a day since it picks some lines at random).
/// Nothing is revealed that the TV wouldn't tell the same morning.
/// </remarks>
internal class LuckIndicatorFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static LuckIndicatorFeature? Instance;

    /// <summary>Where the slot between the weather and the season icons is, from the clock's top-left corner.</summary>
    private static readonly Rectangle Slot = new(164, 68, 48, 32);

    /// <summary>How large the 13 × 13 face is drawn: twice its size fits the slot.</summary>
    private const int Scale = 2;

    /// <summary>The fortune teller's sentence for today, and the day it was worked out for.</summary>
    private string? Forecast;
    private int ForecastDay = -1;

    private static readonly MethodInfo? GetFortuneForecast = AccessTools.Method(typeof(TV), "getFortuneForecast", new[] { typeof(Farmer) });


    /*********
    ** Public methods
    *********/
    public override string Id => "luck-indicator";

    public LuckIndicatorFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }

    /// <summary>Get the face's first animation frame on the cursor sheet for a day's luck, with the TV's thresholds.</summary>
    internal static Rectangle GetFace(double luck)
    {
        if (luck < -0.07)
            return new Rectangle(592, 346, 13, 13);    // very bad: the red skull
        if (luck < -0.02)
            return new Rectangle(540, 346, 13, 13);    // bad: the bat
        if (luck > 0.07)
            return new Rectangle(644, 333, 13, 13);    // very good: the star
        if (luck > 0.02)
            return new Rectangle(592, 333, 13, 13);    // good: the gem
        return new Rectangle(540, 333, 13, 13);        // neutral: the wisp
    }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        this.Postfix(
            AccessTools.Method(typeof(DayTimeMoneyBox), nameof(DayTimeMoneyBox.draw), new[] { typeof(SpriteBatch) }),
            typeof(LuckIndicatorFeature),
            nameof(After_Draw)
        );
    }

    protected override void OnDisable()
    {
        this.Helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
        Instance = null;
    }


    /*********
    ** Patches
    *********/
    /// <summary>Draw the spirit in the clock's slot, and the fortune teller's words when it's hovered.</summary>
    private static void After_Draw(DayTimeMoneyBox __instance, SpriteBatch b)
    {
        if (Instance is null || !Context.IsWorldReady || Game1.eventUp)
            return;

        try
        {
            // the TV's first frame, still: Jordan found the animation distracting on the clock
            Rectangle face = GetFace(Game1.player.DailyLuck);

            Vector2 origin = __instance.position + new Vector2(Slot.X + (Slot.Width - face.Width * Scale) / 2f, Slot.Y + (Slot.Height - face.Height * Scale) / 2f);
            b.Draw(Game1.mouseCursors, origin, face, Color.White, 0f, Vector2.Zero, Scale, SpriteEffects.None, 0.91f);

            Rectangle area = new((int)__instance.position.X + Slot.X, (int)__instance.position.Y + Slot.Y, Slot.Width, Slot.Height);
            if (area.Contains(Game1.getMouseX(), Game1.getMouseY()) && Instance.GetForecast() is { Length: > 0 } text)
                IClickableMenu.drawHoverText(b, Game1.parseText(text, Game1.smallFont, 400), Game1.smallFont);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw the day's luck:\n{ex}", LogLevel.Error);
        }
    }


    /*********
    ** Private methods
    *********/
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.Forecast = null;
    }

    /// <summary>Get what the fortune teller says today, worked out once a day like watching the TV.</summary>
    private string? GetForecast()
    {
        if (this.Forecast is null || this.ForecastDay != Game1.Date.TotalDays)
        {
            this.ForecastDay = Game1.Date.TotalDays;
            // the method only reads the player and the game state: a bare TV is enough to ask it
            object tv = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(TV));
            this.Forecast = GetFortuneForecast?.Invoke(tv, new object[] { Game1.player }) as string ?? "";
            this.Forecast = this.Forecast.Replace("^", " ").Trim();
        }
        return this.Forecast;
    }
}
