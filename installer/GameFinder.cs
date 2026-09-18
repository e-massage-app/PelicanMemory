using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace PelicanMemory.Installer;

/// <summary>Finds the Stardew Valley folder on this PC.</summary>
internal static class GameFinder
{
    /// <summary>The file which proves a folder really is the game folder.</summary>
    private const string GameExecutable = "Stardew Valley.exe";

    /// <summary>Find the game folder, or <c>null</c> if it can't be found automatically.</summary>
    public static string? Find()
    {
        foreach (string candidate in GetCandidates())
        {
            if (IsGameFolder(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Get whether a folder is the Stardew Valley folder.</summary>
    public static bool IsGameFolder(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, GameExecutable));
    }

    /// <summary>Get the places worth looking, best first.</summary>
    private static IEnumerable<string> GetCandidates()
    {
        // Steam and GOG record the install folder when they install the game
        foreach (string key in new[]
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 413150",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 413150",
            @"SOFTWARE\WOW6432Node\GOG.com\Games\1453375253",
            @"SOFTWARE\GOG.com\Games\1453375253"
        })
        {
            foreach (string valueName in new[] { "InstallLocation", "path" })
            {
                string? path = ReadRegistry(key, valueName);
                if (path != null)
                    yield return path;
            }
        }

        // Steam libraries on other drives
        foreach (string library in GetSteamLibraries())
            yield return Path.Combine(library, "steamapps", "common", "Stardew Valley");

        // the usual places, in case nothing was registered
        foreach (string drive in new[] { "C:", "D:", "E:", "F:" })
        {
            yield return $@"{drive}\Program Files (x86)\Steam\steamapps\common\Stardew Valley";
            yield return $@"{drive}\Program Files\Steam\steamapps\common\Stardew Valley";
            yield return $@"{drive}\SteamLibrary\steamapps\common\Stardew Valley";
            yield return $@"{drive}\GOG Games\Stardew Valley";
        }
    }

    /// <summary>Read the Steam library folders from Steam's own config file.</summary>
    private static IEnumerable<string> GetSteamLibraries()
    {
        string? steamPath = ReadRegistry(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
            ?? ReadRegistry(@"SOFTWARE\Valve\Steam", "InstallPath");
        if (steamPath is null)
            yield break;

        yield return steamPath;

        string libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
            yield break;

        foreach (string line in File.ReadLines(libraryFile))
        {
            // lines look like:   "path"   "D:\\SteamLibrary"
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                yield return parts[^1].Replace(@"\\", @"\").Trim();
        }
    }

    private static string? ReadRegistry(string key, string valueName)
    {
        try
        {
            using RegistryKey? registryKey = Registry.LocalMachine.OpenSubKey(key);
            return registryKey?.GetValue(valueName) as string;
        }
        catch
        {
            return null; // a missing or unreadable key just means "look elsewhere"
        }
    }
}
