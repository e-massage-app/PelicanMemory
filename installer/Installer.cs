using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PelicanMemory.Installer;

/// <summary>Installs or updates SMAPI and the mod in the game folder.</summary>
internal class Installer
{
    /*********
    ** Fields
    *********/
    /// <summary>The mod's folder name inside <c>Mods</c>.</summary>
    private const string ModFolderName = "PelicanMemory";

    /// <summary>The settings file kept across updates, so the player's choices survive.</summary>
    private const string ConfigFileName = "config.json";

    private readonly string GamePath;
    private readonly string ModRepository;
    private readonly string ModBranch;


    /*********
    ** Public methods
    *********/
    /// <param name="gamePath">The Stardew Valley folder.</param>
    /// <param name="modRepository">The mod's GitHub repository, like <c>owner/name</c>.</param>
    /// <param name="modBranch">The branch holding the published files.</param>
    public Installer(string gamePath, string modRepository, string modBranch)
    {
        this.GamePath = gamePath;
        this.ModRepository = modRepository;
        this.ModBranch = modBranch;
    }

    /// <summary>Get whether SMAPI is already installed.</summary>
    public bool HasSmapi()
    {
        return File.Exists(Path.Combine(this.GamePath, "StardewModdingAPI.exe"));
    }

    /// <summary>Get the installed mod version, or <c>null</c> if it isn't installed.</summary>
    public string? GetInstalledModVersion()
    {
        string manifest = Path.Combine(this.GamePath, "Mods", ModFolderName, "manifest.json");
        if (!File.Exists(manifest))
            return null;

        foreach (string line in File.ReadLines(manifest))
        {
            if (!line.Contains("\"Version\"", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
                return parts[^1].Trim();
        }

        return null;
    }

    /// <summary>Install SMAPI and the mod, keeping any existing settings.</summary>
    public async Task Run(IProgress<string> progress)
    {
        using Downloader downloader = new();

        // 1. SMAPI
        if (this.HasSmapi())
            progress.Report("SMAPI est déjà installé, on le garde.");
        else
        {
            string installerFolder = await downloader.DownloadSmapiInstaller(progress);
            progress.Report("Installation de SMAPI…");
            this.RunSmapiInstaller(installerFolder);

            if (!this.HasSmapi())
                throw new InvalidOperationException("SMAPI n'a pas pu être installé. Essayez de l'installer à la main depuis smapi.io, puis relancez ce programme.");

            progress.Report("SMAPI est installé.");
        }

        // 2. the mod
        (string version, string modFolder) = await downloader.DownloadMod(this.ModRepository, this.ModBranch, progress);
        progress.Report("Installation du mod…");
        this.CopyMod(modFolder);
        progress.Report($"Pelican Memory {version} est installé.");
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Run SMAPI's own installer without asking the player anything.</summary>
    private void RunSmapiInstaller(string installerFolder)
    {
        string? executable = Directory
            .EnumerateFiles(installerFolder, "SMAPI.Installer.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains("windows", StringComparison.OrdinalIgnoreCase));

        if (executable is null)
            throw new InvalidOperationException("Le programme d'installation de SMAPI est introuvable dans le téléchargement.");

        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true, // SMAPI's installer needs its own console window
            WindowStyle = ProcessWindowStyle.Minimized
        };
        startInfo.ArgumentList.Add("--install");
        startInfo.ArgumentList.Add("--game-path");
        startInfo.ArgumentList.Add(this.GamePath);
        startInfo.ArgumentList.Add("--no-prompt");

        using Process? process = Process.Start(startInfo);
        process?.WaitForExit(milliseconds: 180_000);
    }

    /// <summary>Copy the mod files into the game's Mods folder, keeping the existing settings file.</summary>
    private void CopyMod(string downloadedFolder)
    {
        // the zip contains a single folder with the mod inside
        string source = Directory.EnumerateFiles(downloadedFolder, "manifest.json", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("Le téléchargement ne contient pas de mod (manifest.json introuvable).");
        source = Path.GetDirectoryName(source)!;

        string target = Path.Combine(this.GamePath, "Mods", ModFolderName);
        string? savedConfig = null;
        string configPath = Path.Combine(target, ConfigFileName);

        if (File.Exists(configPath))
            savedConfig = File.ReadAllText(configPath);

        if (Directory.Exists(target))
            Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }

        // put the player's own settings back
        if (savedConfig != null)
            File.WriteAllText(configPath, savedConfig);
    }
}
