using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PelicanMemory.Installer;

/// <summary>The installer window: three steps, a Next button, and nothing to configure.</summary>
internal class WizardForm : Form
{
    /*********
    ** Fields
    *********/
    /// <summary>The mod's GitHub repository, where the published files live.</summary>
    private readonly string ModRepository;

    /// <summary>The branch holding the published files.</summary>
    private readonly string ModBranch;

    private readonly Label Title = new();
    private readonly Panel Content = new();
    private readonly Button BackButton = new();
    private readonly Button NextButton = new();

    // step 1
    private readonly TextBox GamePathBox = new();
    private readonly Label GamePathStatus = new();

    // step 2
    private readonly TextBox Log = new();

    // step 3
    private readonly TextBox LaunchOptionBox = new();

    private readonly Panel[] Steps;
    private int CurrentStep;
    private bool IsInstalling;


    /*********
    ** Public methods
    *********/
    public WizardForm(string modRepository, string modBranch)
    {
        this.ModRepository = modRepository;
        this.ModBranch = modBranch;

        this.Text = "Installer Pelican Memory";
        this.Size = new Size(720, 520);
        this.MinimumSize = this.Size;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font = new Font("Segoe UI", 9.5f);
        this.BackColor = Color.White;

        this.Steps = new[] { this.BuildWelcomeStep(), this.BuildInstallStep(), this.BuildSteamStep() };
        this.BuildFrame();
        this.ShowStep(0);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Build the title, content area and buttons shared by every step.</summary>
    private void BuildFrame()
    {
        this.Title.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        this.Title.Dock = DockStyle.Top;
        this.Title.Height = 56;
        this.Title.Padding = new Padding(24, 16, 24, 0);

        this.Content.Dock = DockStyle.Fill;
        this.Content.Padding = new Padding(24, 8, 24, 8);
        foreach (Panel step in this.Steps)
            this.Content.Controls.Add(step);

        Panel buttons = new() { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(24, 12, 24, 12) };

        this.NextButton.Text = "Suivant";
        this.NextButton.Size = new Size(120, 34);
        this.NextButton.Dock = DockStyle.Right;
        this.NextButton.Click += this.OnNext;

        this.BackButton.Text = "Précédent";
        this.BackButton.Size = new Size(120, 34);
        this.BackButton.Dock = DockStyle.Right;
        this.BackButton.Margin = new Padding(0, 0, 8, 0);
        this.BackButton.Click += (_, _) => this.ShowStep(this.CurrentStep - 1);

        buttons.Controls.Add(this.NextButton);
        buttons.Controls.Add(new Label { Dock = DockStyle.Right, Width = 8 });
        buttons.Controls.Add(this.BackButton);

        this.Controls.Add(this.Content);
        this.Controls.Add(buttons);
        this.Controls.Add(this.Title);
    }

    private Panel BuildWelcomeStep()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Visible = false };

        Label intro = new()
        {
            Text =
                "Ce programme installe Pelican Memory, un mod pour Stardew Valley.\r\n\r\n" +
                "Il s'occupe de tout :\r\n" +
                "   •  il installe SMAPI, le programme qui fait tourner les mods,\r\n" +
                "   •  il installe la dernière version du mod,\r\n" +
                "   •  il vous explique la dernière étape à faire dans Steam.\r\n\r\n" +
                "Vous pouvez relancer ce programme plus tard : il mettra le mod à jour\r\n" +
                "sans toucher à vos réglages ni à vos sauvegardes.",
            Location = new Point(24, 8),
            AutoSize = true
        };

        Label pathLabel = new() { Text = "Dossier du jeu :", Location = new Point(24, 240), AutoSize = true };

        this.GamePathBox.Location = new Point(24, 264);
        this.GamePathBox.Width = 520;
        this.GamePathBox.ReadOnly = true;
        this.GamePathBox.BackColor = Color.White;

        Button browse = new() { Text = "Parcourir…", Location = new Point(552, 262), Size = new Size(100, 27) };
        browse.Click += this.OnBrowse;

        this.GamePathStatus.Location = new Point(24, 298);
        this.GamePathStatus.AutoSize = true;

        panel.Controls.AddRange(new Control[] { intro, pathLabel, this.GamePathBox, browse, this.GamePathStatus });
        return panel;
    }

    private Panel BuildInstallStep()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Visible = false };

        this.Log.Location = new Point(24, 8);
        this.Log.Size = new Size(628, 300);
        this.Log.Multiline = true;
        this.Log.ReadOnly = true;
        this.Log.ScrollBars = ScrollBars.Vertical;
        this.Log.BackColor = Color.White;

        panel.Controls.Add(this.Log);
        return panel;
    }

    private Panel BuildSteamStep()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Visible = false };

        Label steps = new()
        {
            Text =
                "Dernière étape, à faire une seule fois dans Steam :\r\n\r\n" +
                "   1.  Ouvrez Steam.\r\n" +
                "   2.  Clic droit sur Stardew Valley, puis « Propriétés ».\r\n" +
                "   3.  Dans « Options de lancement », collez la ligne ci-dessous.\r\n" +
                "   4.  Fermez la fenêtre. C'est tout.\r\n\r\n" +
                "Ensuite, le bouton « Jouer » de Steam lance le jeu avec le mod,\r\n" +
                "et vous gardez votre temps de jeu, vos succès et vos invitations.",
            Location = new Point(24, 8),
            AutoSize = true
        };

        this.LaunchOptionBox.Location = new Point(24, 236);
        this.LaunchOptionBox.Width = 520;
        this.LaunchOptionBox.ReadOnly = true;

        Button copy = new() { Text = "Copier", Location = new Point(552, 234), Size = new Size(100, 27) };
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(this.LaunchOptionBox.Text);
            MessageBox.Show(this, "La ligne est copiée. Collez-la dans Steam avec Ctrl+V.", "Copié", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        Button openSteam = new() { Text = "Ouvrir Steam", Location = new Point(24, 284), Size = new Size(140, 30) };
        openSteam.Click += (_, _) => TryOpen("steam://open/games");

        panel.Controls.AddRange(new Control[] { steps, this.LaunchOptionBox, copy, openSteam });
        return panel;
    }

    /// <summary>Show a step and update the title and buttons.</summary>
    private void ShowStep(int index)
    {
        if (index < 0 || index >= this.Steps.Length)
            return;

        this.CurrentStep = index;
        for (int i = 0; i < this.Steps.Length; i++)
            this.Steps[i].Visible = i == index;

        this.Title.Text = index switch
        {
            0 => "Bienvenue",
            1 => "Installation",
            _ => "Dernière étape : Steam"
        };
        this.BackButton.Visible = index == 0;
        this.BackButton.Enabled = false;
        this.NextButton.Text = index == this.Steps.Length - 1 ? "Terminer" : "Suivant";

        if (index == 0)
            this.DetectGame();
    }

    private void OnNext(object? sender, EventArgs e)
    {
        switch (this.CurrentStep)
        {
            case 0:
                if (!GameFinder.IsGameFolder(this.GamePathBox.Text))
                {
                    MessageBox.Show(this, "Choisissez d'abord le dossier où Stardew Valley est installé.", "Dossier du jeu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                this.ShowStep(1);
                _ = this.RunInstall();
                break;

            case 1:
                if (this.IsInstalling)
                    return;
                this.ShowStep(2);
                break;

            default:
                this.Close();
                break;
        }
    }

    /// <summary>Find the game folder and tell the player what was found.</summary>
    private void DetectGame()
    {
        if (GameFinder.IsGameFolder(this.GamePathBox.Text))
            return;

        string? path = GameFinder.Find();
        this.GamePathBox.Text = path ?? "";
        this.GamePathStatus.Text = path != null
            ? "Le jeu a été trouvé tout seul, vous n'avez rien à changer."
            : "Le jeu n'a pas été trouvé : cliquez sur « Parcourir… » et choisissez son dossier.";
        this.GamePathStatus.ForeColor = path != null ? Color.ForestGreen : Color.Firebrick;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new() { Description = "Choisissez le dossier de Stardew Valley", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (!GameFinder.IsGameFolder(dialog.SelectedPath))
        {
            MessageBox.Show(this, "Ce dossier ne contient pas Stardew Valley.\r\nIl doit contenir le fichier « Stardew Valley.exe ».", "Mauvais dossier", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        this.GamePathBox.Text = dialog.SelectedPath;
        this.GamePathStatus.Text = "Dossier choisi.";
        this.GamePathStatus.ForeColor = Color.ForestGreen;
    }

    /// <summary>Run the install, writing each step into the log.</summary>
    private async Task RunInstall()
    {
        this.IsInstalling = true;
        this.NextButton.Enabled = false;
        this.Log.Clear();

        string gamePath = this.GamePathBox.Text;
        Progress<string> progress = new(message => this.Log.AppendText(message + "\r\n"));

        try
        {
            Installer installer = new(gamePath, this.ModRepository, this.ModBranch);
            await Task.Run(() => installer.Run(progress));

            this.Log.AppendText("\r\nTout est prêt. Cliquez sur « Suivant ».\r\n");
            this.LaunchOptionBox.Text = $"\"{Path.Combine(gamePath, "StardewModdingAPI.exe")}\" %command%";
        }
        catch (Exception ex)
        {
            this.Log.AppendText("\r\nL'installation a échoué :\r\n" + ex.Message + "\r\n\r\nVérifiez votre connexion internet, puis relancez ce programme.\r\n");
        }
        finally
        {
            this.IsInstalling = false;
            this.NextButton.Enabled = true;
        }
    }

    private static void TryOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Steam may not be installed; the written instructions still apply
        }
    }
}
