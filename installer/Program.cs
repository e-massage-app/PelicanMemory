using System;
using System.Windows.Forms;

namespace PelicanMemory.Installer;

internal static class Program
{
    /// <summary>The GitHub repository the mod is published from. Change this if the repository moves.</summary>
    private const string ModRepository = "e-massage-app/PelicanMemory";

    /// <summary>The branch holding the published files.</summary>
    private const string ModBranch = "main";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new WizardForm(ModRepository, ModBranch));
    }
}
