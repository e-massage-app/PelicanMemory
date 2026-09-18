using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace PelicanMemory.Core;

/// <summary>Reads and writes the mod's settings in <c>config.json</c>, saving on every change.</summary>
internal class ModSettings
{
    private readonly IModHelper Helper;
    private readonly ModConfig Config;

    public ModSettings(IModHelper helper)
    {
        this.Helper = helper;
        this.Config = helper.ReadConfig<ModConfig>();
    }

    /// <summary>Get whether a feature is enabled.</summary>
    /// <param name="featureId">The feature ID.</param>
    /// <param name="defaultValue">The value to use when the player has never toggled it.</param>
    public bool IsFeatureEnabled(string featureId, bool defaultValue)
    {
        return this.Config.Features.TryGetValue(featureId, out bool enabled)
            ? enabled
            : defaultValue;
    }

    /// <summary>Set whether a feature is enabled.</summary>
    public void SetFeatureEnabled(string featureId, bool enabled)
    {
        this.Config.Features[featureId] = enabled;
        this.Save();
    }

    /// <summary>Get a numeric setting, like a percentage.</summary>
    /// <param name="key">The setting key, e.g. <c>minimap.opacity</c>.</param>
    /// <param name="defaultValue">The value to use when it was never set.</param>
    public int GetNumber(string key, int defaultValue)
    {
        return this.Config.Numbers.TryGetValue(key, out int value)
            ? value
            : defaultValue;
    }

    /// <summary>Get an on/off setting which isn't a feature toggle.</summary>
    /// <param name="key">The setting key, e.g. <c>map-labels.visible</c>.</param>
    /// <param name="defaultValue">The value to use when it was never set.</param>
    public bool GetFlag(string key, bool defaultValue)
    {
        return this.Config.Flags.TryGetValue(key, out bool value)
            ? value
            : defaultValue;
    }

    /// <summary>Set an on/off setting.</summary>
    public void SetFlag(string key, bool value)
    {
        this.Config.Flags[key] = value;
        this.Save();
    }

    /// <summary>Set a numeric setting.</summary>
    public void SetNumber(string key, int value)
    {
        this.Config.Numbers[key] = value;
        this.Save();
    }

    /// <summary>Get a keybind, writing the default into the config so the player can see and change it.</summary>
    /// <param name="key">The keybind key, e.g. <c>farm-layers</c>.</param>
    /// <param name="defaultValue">The default keybind, in SMAPI's format (e.g. <c>F3</c>).</param>
    public KeybindList GetKeybind(string key, string defaultValue)
    {
        if (!this.Config.Keybinds.TryGetValue(key, out string? raw))
        {
            this.Config.Keybinds[key] = raw = defaultValue;
            this.Save();
        }

        return KeybindList.TryParse(raw, out KeybindList? parsed, out _)
            ? parsed
            : KeybindList.Parse(defaultValue);
    }

    private void Save()
    {
        this.Helper.WriteConfig(this.Config);
    }
}
