using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using System.Linq;
using StardewModdingAPI;
using StardewValley.Menus;

namespace PelicanMemory.Core;

/// <summary>Base class handling the active state and Harmony patch bookkeeping for a feature.</summary>
internal abstract class FeatureBase : IFeature
{
    /// <summary>The patches applied by this feature, so they can be removed precisely on disable.</summary>
    private readonly List<(MethodBase Original, MethodInfo Patch)> Patches = new();

    protected IModHelper Helper { get; }
    protected IMonitor Monitor { get; }

    /// <summary>The mod's settings, for features which have their own.</summary>
    protected ModSettings Settings { get; }

    private Harmony Harmony { get; }

    public abstract string Id { get; }
    public virtual bool EnabledByDefault => true;
    public bool IsActive { get; private set; }

    protected FeatureBase(IModHelper helper, IMonitor monitor, Harmony harmony, ModSettings settings)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.Harmony = harmony;
        this.Settings = settings;
    }

    /// <inheritdoc />
    public virtual IEnumerable<OptionsElement> CreateOptionRows()
    {
        return Enumerable.Empty<OptionsElement>();
    }

    public void Enable()
    {
        if (this.IsActive)
            return;

        this.OnEnable();
        this.IsActive = true;
    }

    public void Disable()
    {
        if (!this.IsActive)
            return;

        this.OnDisable();

        // unpatch only our own patch methods, so other mods' patches on the same methods are untouched
        foreach (var (original, patch) in this.Patches)
            this.Harmony.Unpatch(original, patch);
        this.Patches.Clear();

        this.IsActive = false;
    }

    /// <summary>Subscribe to events and apply patches.</summary>
    protected abstract void OnEnable();

    /// <summary>Unsubscribe from events. Patches added through <see cref="Prefix"/>/<see cref="Postfix"/> are removed automatically.</summary>
    protected virtual void OnDisable() { }

    /// <summary>Apply a prefix patch which is removed when the feature is disabled.</summary>
    protected void Prefix(MethodBase original, Type patchType, string patchMethod)
    {
        MethodInfo patch = AccessTools.Method(patchType, patchMethod) ?? throw new InvalidOperationException($"Can't find patch method {patchType.Name}.{patchMethod}.");
        this.Harmony.Patch(original, prefix: new HarmonyMethod(patch));
        this.Patches.Add((original, patch));
    }

    /// <summary>Apply a postfix patch which is removed when the feature is disabled.</summary>
    protected void Postfix(MethodBase original, Type patchType, string patchMethod)
    {
        MethodInfo patch = AccessTools.Method(patchType, patchMethod) ?? throw new InvalidOperationException($"Can't find patch method {patchType.Name}.{patchMethod}.");
        this.Harmony.Patch(original, postfix: new HarmonyMethod(patch));
        this.Patches.Add((original, patch));
    }
}
