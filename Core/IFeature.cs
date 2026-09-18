using System.Collections.Generic;
using StardewValley.Menus;

namespace PelicanMemory.Core;

/// <summary>A self-contained feature which can be toggled at runtime from the mod's menu tab.</summary>
/// <remarks>
/// Design rule for every feature: never show information the player hasn't already obtained in their own save.
/// Filter everything through the local player's real progression (visited locations, fish caught, etc.).
/// </remarks>
internal interface IFeature
{
    /// <summary>A unique, stable ID. Used as the config key and to build translation keys (<c>feature.{Id}.name</c> / <c>feature.{Id}.description</c>).</summary>
    string Id { get; }

    /// <summary>Whether the feature is enabled when the player has never toggled it.</summary>
    bool EnabledByDefault { get; }

    /// <summary>Whether the feature is currently hooked into the game.</summary>
    bool IsActive { get; }

    /// <summary>Hook into the game (events, patches). Safe to call when already active.</summary>
    void Enable();

    /// <summary>Unhook everything added by <see cref="Enable"/>. Safe to call when already inactive.</summary>
    void Disable();

    /// <summary>Extra settings shown under the feature's checkbox in the mod's menu tab, like a slider.</summary>
    IEnumerable<OptionsElement> CreateOptionRows();
}
