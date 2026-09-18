using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace PelicanMemory.Core;

/// <summary>Owns every feature and keeps their active state in sync with the config.</summary>
internal class FeatureRegistry
{
    private readonly List<IFeature> FeaturesImpl = new();
    private readonly IModHelper Helper;
    private readonly IMonitor Monitor;
    private readonly ModSettings Settings;

    /// <summary>The registered features, in display order.</summary>
    public IReadOnlyList<IFeature> Features => this.FeaturesImpl;

    public FeatureRegistry(IModHelper helper, IMonitor monitor, ModSettings settings)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.Settings = settings;
    }

    /// <summary>Register a feature and enable it if its config says so.</summary>
    public void Add(IFeature feature)
    {
        this.FeaturesImpl.Add(feature);
        if (this.IsEnabled(feature))
            this.TrySetActive(feature, true);
    }

    /// <summary>Get whether a feature is enabled in the config.</summary>
    public bool IsEnabled(IFeature feature)
    {
        return this.Settings.IsFeatureEnabled(feature.Id, feature.EnabledByDefault);
    }

    /// <summary>Enable or disable a feature immediately and persist the choice.</summary>
    public void SetEnabled(IFeature feature, bool enabled)
    {
        this.Settings.SetFeatureEnabled(feature.Id, enabled);
        this.TrySetActive(feature, enabled);
    }

    /// <summary>Get the translated display name for a feature.</summary>
    public string GetName(IFeature feature) => this.Translate($"feature.{feature.Id}.name");

    /// <summary>Get the translated description for a feature.</summary>
    public string GetDescription(IFeature feature) => this.Translate($"feature.{feature.Id}.description");

    /// <summary>Get a translation from the mod's <c>i18n</c> folder.</summary>
    public string Translate(string key) => this.Helper.Translation.Get(key);

    private void TrySetActive(IFeature feature, bool active)
    {
        try
        {
            if (active)
                feature.Enable();
            else
                feature.Disable();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to {(active ? "enable" : "disable")} feature '{feature.Id}':\n{ex}", LogLevel.Error);
        }
    }
}
