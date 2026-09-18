using HarmonyLib;
using PelicanMemory.Core;
using PelicanMemory.Features.AnimalCare;
using PelicanMemory.Features.CaughtFishTooltip;
using PelicanMemory.Features.CommunityCenterHints;
using PelicanMemory.Features.FarmLayers;
using PelicanMemory.Features.MuseumHints;
using PelicanMemory.Features.Minimap;
using PelicanMemory.Features.SocialLocations;
using PelicanMemory.Features.VisitedMapLabels;
using PelicanMemory.UI;
using StardewModdingAPI;

namespace PelicanMemory;

/// <summary>The mod entry point.</summary>
internal class ModEntry : Mod
{
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModSettings settings = new(helper);
        Harmony harmony = new(this.ModManifest.UniqueID);
        FeatureRegistry registry = new(helper, this.Monitor, settings);

        // To add a feature: create a class implementing IFeature (usually via FeatureBase),
        // add its name/description to i18n, and register it here. The menu tab lists it automatically.
        registry.Add(new VisitedMapLabelsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new CaughtFishTooltipFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new MinimapFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new SocialLocationsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new CommunityCenterHintsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new MuseumHintsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new FarmLayersFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new AnimalCareFeature(helper, this.Monitor, harmony, settings));

        GameMenuTab.Apply(harmony, this.Monitor, registry);
        TooltipBadges.Apply(harmony, this.Monitor);
    }
}
