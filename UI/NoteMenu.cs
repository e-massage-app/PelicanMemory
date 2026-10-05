using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace PelicanMemory.UI;

/// <summary>The game's naming window, set up for a short personal note.</summary>
/// <remarks>
/// The vanilla window is made for names: a narrow box, a dice for random villager names, a swear filter, at least one
/// letter, and no way out with Escape. Here the box is wide, the dice is hidden, the text is kept as typed, an empty
/// note is allowed (it means "remove"), and Escape cancels.
/// </remarks>
internal class NoteMenu : NamingMenu
{
    /*********
    ** Fields
    *********/
    /// <summary>The width of the text box.</summary>
    private const int BoxWidth = 512;


    /*********
    ** Public methods
    *********/
    /// <param name="onDone">Called with the note once confirmed; an empty note means the player cleared it.</param>
    /// <param name="title">The question shown above the box.</param>
    /// <param name="note">The note to start from.</param>
    /// <param name="maxLength">The most characters allowed.</param>
    public NoteMenu(doneNamingBehavior onDone, string title, string note, int maxLength)
        : base(onDone, title, note)
    {
        this.minLength = 0;
        this.FilterInput = false;
        this.randomButton.visible = false;

        this.textBox.textLimit = maxLength;
        this.textBox.Width = BoxWidth;
        this.textBox.X = Game1.uiViewport.Width / 2 - BoxWidth / 2 - 48;
        this.textBox.Text = note;
        this.textBoxCC.bounds.X = this.textBox.X;
        this.textBoxCC.bounds.Width = BoxWidth;
        this.doneNamingButton.bounds.X = this.textBox.X + this.textBox.Width + 36;
    }

    /// <inheritdoc />
    public override void receiveKeyPress(Keys key)
    {
        // the game's window swallows Escape: let it cancel
        if (key == Keys.Escape)
        {
            this.textBox.Selected = false;
            this.exitThisMenu();
            return;
        }

        base.receiveKeyPress(key);
    }
}
