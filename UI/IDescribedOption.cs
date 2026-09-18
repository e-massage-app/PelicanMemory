namespace PelicanMemory.UI;

/// <summary>An option row which shows a description when the player hovers it.</summary>
internal interface IDescribedOption
{
    /// <summary>The description shown on hover.</summary>
    string Description { get; }
}
