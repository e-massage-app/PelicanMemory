using System;
using HarmonyLib;
using PelicanMemory.Core;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.Features.SkillExperience;

/// <summary>Says how much experience a skill has, and how much is left before the next level.</summary>
/// <remarks>
/// The game counts this experience but never shows it: the ten squares say roughly where you are, not how far the next
/// one is. It is the player's own progress and nothing else, so there is nothing to hide — only a number to give back.
/// </remarks>
internal class SkillExperienceFeature : FeatureBase
{
    /*********
    ** Fields
    *********/
    /// <summary>The active instance, for the static Harmony patch.</summary>
    private static SkillExperienceFeature? Instance;

    /// <summary>The highest level a skill can reach.</summary>
    private const int MaxLevel = 10;


    /*********
    ** Public methods
    *********/
    public override string Id => "skill-xp";

    public SkillExperienceFeature(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
        : base(helper, monitor, harmony, settings) { }


    /*********
    ** Protected methods
    *********/
    protected override void OnEnable()
    {
        Instance = this;

        this.Postfix(
            AccessTools.Method(typeof(SkillsPage), nameof(SkillsPage.performHoverAction)),
            typeof(SkillExperienceFeature),
            nameof(After_PerformHoverAction)
        );
    }

    protected override void OnDisable()
    {
        Instance = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Add the experience line to the tooltip of the hovered skill.</summary>
    private static void After_PerformHoverAction(SkillsPage __instance, int x, int y)
    {
        if (Instance is null || !Context.IsWorldReady)
            return;

        try
        {
            foreach (ClickableTextureComponent area in __instance.skillAreas)
            {
                if (!area.containsPoint(x, y) || !int.TryParse(area.name, out int skill))
                    continue;

                IReflectedField<string> hoverText = Instance.Helper.Reflection.GetField<string>(__instance, "hoverText");
                hoverText.SetValue(Instance.Describe(skill) + Environment.NewLine + Environment.NewLine + hoverText.GetValue());
                return;
            }
        }
        catch (Exception ex)
        {
            Instance.Monitor.LogOnce($"Failed to describe a skill's experience:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Describe where a skill stands, and what is left to reach the next level.</summary>
    private string Describe(int skill)
    {
        int experience = Game1.player.experiencePoints[skill];
        int level = Game1.player.GetUnmodifiedSkillLevel(skill);

        if (level >= MaxLevel)
            return this.Helper.Translation.Get("skill-xp.maxed", new { total = Format(experience) });

        // the squares in front of the player show whole levels, so the numbers are given for the level being filled
        // (the game returns -1 rather than 0 for level 0, which would shift every number by one)
        int start = level > 0 ? Farmer.getBaseExperienceForLevel(level) : 0;
        int next = Farmer.getBaseExperienceForLevel(level + 1);

        return this.Helper.Translation.Get("skill-xp.progress", new
        {
            done = Format(experience - start),
            needed = Format(next - start),
            left = Format(Math.Max(0, next - experience)),
            level = level + 1
        });
    }

    /// <summary>Format a number with spaced thousands, which is how the game writes its own.</summary>
    private static string Format(int value)
    {
        return value.ToString("N0");
    }
}
