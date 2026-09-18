using System;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>A vanilla options checkbox bound to a callback instead of <see cref="Options"/>.</summary>
internal class FeatureCheckbox : OptionsCheckbox, IDescribedOption
{
    /// <summary>An option ID the vanilla <see cref="Options"/> ignores, so the base checkbox doesn't read or write game options.</summary>
    private const int UnusedOptionId = 90_000;

    private readonly Action<bool> OnToggled;

    /// <inheritdoc />
    public string Description { get; }

    public FeatureCheckbox(string label, string description, bool isChecked, Action<bool> onToggled)
        : base(label, UnusedOptionId)
    {
        this.Description = description;
        this.isChecked = isChecked;
        this.OnToggled = onToggled;
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y)
    {
        if (this.greyedOut)
            return;

        Game1.playSound("drumkit6");
        this.isChecked = !this.isChecked;
        this.OnToggled(this.isChecked);
    }
}
