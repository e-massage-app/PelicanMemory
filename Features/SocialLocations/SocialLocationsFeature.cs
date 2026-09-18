using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.SocialLocations;

/// <summary>Shows where each villager currently is, in the social tab of the game menu.</summary>
/// <remarks>
/// Anti-spoil: only villagers the player has met are handled (the vanilla page hides the others anyway), and the
/// place is only named if the player has already been there. Otherwise it just says the place is unknown.
/// </remarks>
internal class SocialLocationsFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static SocialLocationsFeature? Instance;

    /// <summary>The text scale, small enough to fit under the hearts.</summary>
    private const float TextScale = 0.75f;

    /// <summary>The text for each location, since the social page redraws every frame.</summary>
    private readonly Dictionary<string, string> TextCache = new();

    /// <summary>The number of visited locations when <see cref="TextCache"/> was filled.</summary>
    private int CachedVisitedCount = -1;


    /*********
    ** Public methods
    *********/
    public override string Id => "social-locations";

    public SocialLocationsFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;
        this.Postfix(
            AccessTools.Method(typeof(SocialPage), nameof(SocialPage.drawNPCSlot)),
            typeof(SocialLocationsFeature),
            nameof(After_DrawNpcSlot)
        );
    }

    protected override void OnDisable()
    {
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the villager's current place under their hearts.</summary>
    private static void After_DrawNpcSlot(SocialPage __instance, SpriteBatch b, int i)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            SocialPage.SocialEntry? entry = __instance.GetSocialEntry(i);
            if (entry is null || entry.IsPlayer || !entry.IsMet || entry.Character is not NPC npc)
                return;

            GameLocation? location = npc.currentLocation;
            if (location is null || i >= __instance.sprites.Count)
                return;

            string text = Instance.GetLocationText(location);

            // under the hearts, which take a second row for characters with more than 10
            int maxHearts = Math.Max(Utility.GetMaximumHeartsForCharacter(npc), 10);
            Vector2 position = new(
                __instance.xPositionOnScreen + 316,
                __instance.sprites[i].bounds.Y + (maxHearts > 10 ? 96 : 72)
            );

            b.DrawString(Game1.smallFont, Instance.Truncate(text, 296), position, Game1.textColor * 0.9f, 0f, Vector2.Zero, TextScale, SpriteEffects.None, 0.88f);
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to draw villager locations in the social tab:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the text for a villager's current place, hiding places the player has never visited.</summary>
    private string GetLocationText(GameLocation location)
    {
        // visits rarely change, but the text must follow them
        int visitedCount = Game1.player.locationsVisited.Count;
        if (visitedCount != this.CachedVisitedCount)
        {
            this.TextCache.Clear();
            this.CachedVisitedCount = visitedCount;
        }

        if (!this.TextCache.TryGetValue(location.Name, out string? text))
            this.TextCache[location.Name] = text = this.BuildLocationText(location);

        return text;
    }

    private string BuildLocationText(GameLocation location)
    {
        // a place the player knows
        if (Game1.player.locationsVisited.Contains(location.Name))
            return this.Helper.Translation.Get("social.location", new { value = location.DisplayName ?? location.Name });

        // else the surrounding area, if the player has been there (e.g. "Mountains" rather than "Carpenter's Shop")
        if (WorldMapLookup.TryGetKnownAreaName(location, Game1.player, out string areaName))
            return this.Helper.Translation.Get("social.location-area", new { value = areaName });

        return this.Helper.Translation.Get("social.location-unknown");
    }

    /// <summary>Shorten text which doesn't fit in the available width.</summary>
    private string Truncate(string text, int maxWidth)
    {
        if (Game1.smallFont.MeasureString(text).X * TextScale <= maxWidth)
            return text;

        while (text.Length > 1 && Game1.smallFont.MeasureString(text + "…").X * TextScale > maxWidth)
            text = text[..^1];
        return text + "…";
    }
}
