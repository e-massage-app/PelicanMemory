using System;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>A vanilla options slider (0-100) bound to a callback instead of <see cref="Options"/>.</summary>
internal class FeatureSlider : OptionsSlider, IDescribedOption
{
    /// <summary>An option ID the vanilla <see cref="Options"/> ignores, so the base slider doesn't read or write game options.</summary>
    private const int UnusedOptionId = 90_001;

    private readonly Action<int> OnChanged;

    /// <inheritdoc />
    public string Description { get; }

    public FeatureSlider(string label, string description, int value, Action<int> onChanged, int x = -1, int y = -1)
        : base(label, UnusedOptionId, x, y)
    {
        this.Description = description;
        this.value = value;
        this.OnChanged = onChanged;
    }

    /// <inheritdoc />
    public override void leftClickHeld(int x, int y)
    {
        if (this.greyedOut)
            return;

        int oldValue = this.value;
        base.leftClickHeld(x, y);
        if (this.value != oldValue)
            this.OnChanged(this.value);
    }

    /// <inheritdoc />
    public override void receiveKeyPress(Keys key)
    {
        int oldValue = this.value;
        base.receiveKeyPress(key);
        if (this.value != oldValue)
            this.OnChanged(this.value);
    }
}
