using System.Collections.Generic;

namespace PelicanMemory.Core;

/// <summary>The mod's global config (<c>config.json</c>).</summary>
internal class ModConfig
{
    /// <summary>The enabled state for each feature, indexed by feature ID. Features missing here use their default.</summary>
    public Dictionary<string, bool> Features { get; set; } = new();

    /// <summary>Numeric settings like percentages, indexed by key (e.g. <c>minimap.opacity</c>).</summary>
    public Dictionary<string, int> Numbers { get; set; } = new();

    /// <summary>On/off settings which aren't a feature toggle, indexed by key (e.g. <c>map-labels.visible</c>).</summary>
    public Dictionary<string, bool> Flags { get; set; } = new();

    /// <summary>The keys which toggle a feature, indexed by key (e.g. <c>farm-layers</c>). Uses SMAPI's keybind format.</summary>
    public Dictionary<string, string> Keybinds { get; set; } = new();
}
