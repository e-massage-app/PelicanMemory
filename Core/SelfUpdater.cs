using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace PelicanMemory.Core;

/// <summary>What changed in one published version.</summary>
/// <param name="Version">The version number, like <c>1.3.0</c>.</param>
/// <param name="Fr">The changes, in French.</param>
/// <param name="En">The changes, in English.</param>
internal record ChangelogEntry(string Version, string[] Fr, string[] En);

/// <summary>Fetches a newer version of the mod and installs it over the running one.</summary>
/// <remarks>
/// <para>
/// The update is the mod's own zip (a few hundred kilobytes), read from the same place the installer reads the
/// version number. The installer is only needed once, to install SMAPI; after that the mod keeps itself up to date.
/// </para>
/// <para>
/// Windows won't let a loaded DLL be overwritten, but it does let it be renamed. So each file is moved aside as
/// <c>*.old</c> and the new one written under the original name: the running game keeps using the old code, and the
/// next launch loads the new one and deletes the leftovers. One restart, no helper program. If anything fails
/// half-way, every file moved so far is put back.
/// </para>
/// </remarks>
internal class SelfUpdater
{
    /*********
    ** Fields
    *********/
    /// <summary>Where releases are published when <c>config.json</c> doesn't say otherwise.</summary>
    private const string DefaultSource = "https://raw.githubusercontent.com/e-massage-app/PelicanMemory/main/dist/";

    /// <summary>The file listing every published version and its changes, newest first.</summary>
    private const string ChangelogFile = "update.json";

    /// <summary>The mod's zip, as built by the mod build package.</summary>
    private const string PackageFile = "PelicanMemory.zip";

    /// <summary>The folder a downloaded package is unpacked into before being checked.</summary>
    private const string StagingFolder = ".update";

    /// <summary>The suffix of the files moved aside during an update.</summary>
    private const string OldSuffix = ".old";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IManifest Manifest;
    private readonly string ModFolder;
    private readonly string Source;


    /*********
    ** Public methods
    *********/
    /// <param name="manifest">The running mod's manifest.</param>
    /// <param name="modFolder">The folder the mod is installed in.</param>
    /// <param name="source">A web address or a local folder to read releases from, or <c>null</c> for the published ones.</param>
    public SelfUpdater(IManifest manifest, string modFolder, string? source)
    {
        this.Manifest = manifest;
        this.ModFolder = modFolder;
        this.Source = string.IsNullOrWhiteSpace(source) ? DefaultSource : source.Trim();
    }

    /// <summary>Delete what the last update left behind, now that the game has loaded the new files.</summary>
    public void CleanUp(IMonitor monitor)
    {
        foreach (string file in Directory.EnumerateFiles(this.ModFolder, "*" + OldSuffix, SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                monitor.Log($"Couldn't delete {file} left by the last update: {ex.Message}", LogLevel.Trace);
            }
        }

        try
        {
            string staging = Path.Combine(this.ModFolder, StagingFolder);
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception ex)
        {
            monitor.Log($"Couldn't delete the update staging folder: {ex.Message}", LogLevel.Trace);
        }
    }

    /// <summary>Get the published versions newer than the running one, newest first, or an empty list if there are none.</summary>
    public async Task<IReadOnlyList<ChangelogEntry>> GetNewerVersionsAsync()
    {
        byte[] raw = await this.ReadAsync(ChangelogFile);
        ChangelogEntry[] entries = JsonSerializer.Deserialize<ChangelogEntry[]>(raw, JsonOptions) ?? Array.Empty<ChangelogEntry>();

        return entries
            .Where(entry => SemanticVersion.TryParse(entry.Version, out ISemanticVersion? version) && version.IsNewerThan(this.Manifest.Version))
            .OrderByDescending(entry => (ISemanticVersion)new SemanticVersion(entry.Version), Comparer<ISemanticVersion>.Create((a, b) => a.CompareTo(b)))
            .ToList();
    }

    /// <summary>Download a version and install it over the running one.</summary>
    /// <param name="version">The version the downloaded package must contain.</param>
    /// <exception cref="InvalidDataException">The package isn't the expected version of this mod.</exception>
    public async Task InstallAsync(string version)
    {
        byte[] package = await this.ReadAsync(PackageFile);
        await Task.Run(() => this.Install(package, version));
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Read a published file, from the web or from a local folder.</summary>
    private async Task<byte[]> ReadAsync(string file)
    {
        if (Directory.Exists(this.Source))
            return await File.ReadAllBytesAsync(Path.Combine(this.Source, file));

        return await Http.GetByteArrayAsync(this.Source.TrimEnd('/') + "/" + file);
    }

    /// <summary>Unpack a package, check it, then swap its files in.</summary>
    private void Install(byte[] package, string version)
    {
        string staging = Path.Combine(this.ModFolder, StagingFolder);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);

        try
        {
            // the extraction refuses any entry that would land outside the staging folder
            using (ZipArchive archive = new(new MemoryStream(package)))
                archive.ExtractToDirectory(staging);

            string root = this.FindPackageRoot(staging, version);
            this.SwapFiles(root);
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); }
            catch { /* removed on next launch */ }
        }
    }

    /// <summary>Find the folder holding the package's manifest, and check it's the expected version of this mod.</summary>
    private string FindPackageRoot(string staging, string version)
    {
        string? manifestPath = Directory
            .EnumerateFiles(staging, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        if (manifestPath is null)
            throw new InvalidDataException("The package has no manifest.");

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        string? id = manifest.RootElement.TryGetProperty("UniqueID", out JsonElement idValue) ? idValue.GetString() : null;
        string? packageVersion = manifest.RootElement.TryGetProperty("Version", out JsonElement versionValue) ? versionValue.GetString() : null;
        string? entryDll = manifest.RootElement.TryGetProperty("EntryDll", out JsonElement dllValue) ? dllValue.GetString() : null;

        if (!string.Equals(id, this.Manifest.UniqueID, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The package is for another mod ({id}).");
        if (packageVersion != version)
            throw new InvalidDataException($"The package holds version {packageVersion}, not {version}.");

        string root = Path.GetDirectoryName(manifestPath)!;
        if (entryDll is null || !File.Exists(Path.Combine(root, entryDll)))
            throw new InvalidDataException("The package has no mod DLL.");

        return root;
    }

    /// <summary>Move each current file aside and put the new one in its place, undoing everything if one fails.</summary>
    private void SwapFiles(string root)
    {
        List<(string Target, string? Old)> done = new();

        try
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file);

                // the player's settings are never part of a release, and never touched by one
                if (relative.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                string target = Path.Combine(this.ModFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                string? old = null;
                if (File.Exists(target))
                {
                    // a unique name, in case an earlier leftover is still locked
                    old = $"{target}.{DateTime.Now.Ticks}{OldSuffix}";
                    File.Move(target, old);
                }

                done.Add((target, old));
                File.Copy(file, target);
            }
        }
        catch
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                (string target, string? old) = done[i];
                try
                {
                    if (File.Exists(target))
                        File.Delete(target);
                    if (old != null)
                        File.Move(old, target);
                }
                catch
                {
                    // keep going: every other file should still be put back
                }
            }

            throw;
        }
    }
}
