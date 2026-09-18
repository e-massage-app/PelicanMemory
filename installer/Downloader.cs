using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace PelicanMemory.Installer;

/// <summary>Downloads SMAPI and the mod from their official releases.</summary>
internal class Downloader : IDisposable
{
    /*********
    ** Fields
    *********/
    private readonly HttpClient Client = new();

    /// <summary>Where SMAPI is published.</summary>
    private const string SmapiRepository = "Pathoschild/SMAPI";


    /*********
    ** Public methods
    *********/
    public Downloader()
    {
        // GitHub requires a user agent, and rejects anonymous calls without one
        this.Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PelicanMemory-Installer", "1.0"));
        this.Client.Timeout = TimeSpan.FromMinutes(5);
    }

    /// <summary>Download the latest SMAPI installer and unzip it into a temporary folder.</summary>
    /// <returns>Returns the folder containing the unzipped installer.</returns>
    public async Task<string> DownloadSmapiInstaller(IProgress<string> progress)
    {
        (string version, string url) = await this.GetLatestRelease(SmapiRepository, asset => asset.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !asset.Contains("double-zipped", StringComparison.OrdinalIgnoreCase));
        progress.Report($"Téléchargement de SMAPI {version}…");

        string folder = this.CreateTempFolder("smapi");
        await this.DownloadAndExtract(url, folder);
        return folder;
    }

    /// <summary>Download the published mod and unzip it into a temporary folder.</summary>
    /// <param name="repository">The mod's GitHub repository, like <c>owner/name</c>.</param>
    /// <param name="branch">The branch holding the published files.</param>
    /// <returns>Returns the version and the folder containing the unzipped mod.</returns>
    /// <remarks>
    /// The mod is published as two files in the repository's <c>dist</c> folder rather than as a GitHub release:
    /// a plain <c>git push</c> is enough to ship an update, with no extra tool or token involved.
    /// </remarks>
    public async Task<(string Version, string Folder)> DownloadMod(string repository, string branch, IProgress<string> progress)
    {
        string baseUrl = $"https://raw.githubusercontent.com/{repository}/{branch}/dist";

        string version = (await this.Client.GetStringAsync($"{baseUrl}/version.txt")).Trim();
        progress.Report($"Téléchargement de Pelican Memory {version}…");

        string folder = this.CreateTempFolder("mod");
        await this.DownloadAndExtract($"{baseUrl}/PelicanMemory.zip", folder);
        return (version, folder);
    }

    public void Dispose()
    {
        this.Client.Dispose();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the version and download URL of a repository's latest release.</summary>
    private async Task<(string Version, string Url)> GetLatestRelease(string repository, Func<string, bool> matchAsset)
    {
        string json = await this.Client.GetStringAsync($"https://api.github.com/repos/{repository}/releases/latest");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string version = root.TryGetProperty("tag_name", out JsonElement tag) ? tag.GetString() ?? "?" : "?";

        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (matchAsset(name))
                return (version, asset.GetProperty("browser_download_url").GetString()!);
        }

        throw new InvalidOperationException($"La dernière version de {repository} ne contient pas de fichier .zip téléchargeable.");
    }

    /// <summary>Download a zip and extract it.</summary>
    private async Task DownloadAndExtract(string url, string targetFolder)
    {
        string zipPath = Path.Combine(targetFolder, "download.zip");

        await using (Stream source = await this.Client.GetStreamAsync(url))
        await using (FileStream file = File.Create(zipPath))
            await source.CopyToAsync(file);

        ZipFile.ExtractToDirectory(zipPath, targetFolder);
        File.Delete(zipPath);
    }

    private string CreateTempFolder(string name)
    {
        string folder = Path.Combine(Path.GetTempPath(), "PelicanMemoryInstaller", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
