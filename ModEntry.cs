using HarmonyLib;
using PelicanMemory.Core;
using PelicanMemory.Features.AnimalCare;
using PelicanMemory.Features.CaughtFishTooltip;
using PelicanMemory.Features.ChestNames;
using PelicanMemory.Features.ChestSearch;
using PelicanMemory.Features.CommunityCenterHints;
using PelicanMemory.Features.CraftFromChests;
using PelicanMemory.Features.CraftingFilters;
using PelicanMemory.Features.CropTimer;
using PelicanMemory.Features.DepositEverywhere;
using PelicanMemory.Features.FarmLayers;
using PelicanMemory.Features.FishHints;
using PelicanMemory.Features.HorseActions;
using PelicanMemory.Features.ItemSearch;
using PelicanMemory.Features.MachineTimer;
using PelicanMemory.Features.MapPins;
using PelicanMemory.Features.MuseumHints;
using PelicanMemory.Features.PhoneOrders;
using PelicanMemory.Features.NightRecap;
using PelicanMemory.Features.PurchaseConfirm;
using PelicanMemory.Features.RecipeLookup;
using PelicanMemory.Features.Minimap;
using PelicanMemory.Features.MoveObjects;
using PelicanMemory.Features.SelfUpdate;
using PelicanMemory.Features.SkillExperience;
using PelicanMemory.Features.SocialLocations;
using PelicanMemory.Features.TransferQuantity;
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
        StorageIndex storage = new(helper);
        PlayerStore store = new(this.ModManifest.UniqueID);

        // the files moved aside by the last update can only be deleted now that the game has loaded the new ones
        SelfUpdater updater = new(this.ModManifest, helper.DirectoryPath, settings.UpdateSource);
        updater.CleanUp(this.Monitor);

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
        registry.Add(new CropTimerFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new MachineTimerFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new NightRecapFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new PurchaseConfirmFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new PhoneOrdersFeature(helper, this.Monitor, harmony, settings, storage));
        registry.Add(new TransferQuantityFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new HorseActionsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new DepositEverywhereFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new MoveObjectsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new RecipeLookupFeature(helper, this.Monitor, harmony, settings, storage));
        registry.Add(new ChestSearchFeature(helper, this.Monitor, harmony, settings, storage));
        registry.Add(new ItemSearchFeature(helper, this.Monitor, harmony, settings, storage, store));
        registry.Add(new ChestNamesFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new MapPinsFeature(helper, this.Monitor, harmony, settings, store));
        registry.Add(new FishHintsFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new SkillExperienceFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new CraftFromChestsFeature(helper, this.Monitor, harmony, settings, storage));
        registry.Add(new CraftingFiltersFeature(helper, this.Monitor, harmony, settings));
        registry.Add(new SelfUpdateFeature(helper, this.Monitor, harmony, settings, updater, this.ModManifest.Version));

        GameMenuTab.Apply(harmony, this.Monitor, registry);
        WorldTooltip.Attach(helper.Events, this.Monitor);
        TooltipBadges.Apply(harmony, this.Monitor);
    }
}
