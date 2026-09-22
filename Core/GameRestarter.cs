using System;
using System.Diagnostics;
using System.IO;
using StardewModdingAPI;

namespace PelicanMemory.Core;

/// <summary>Starts the game again once this instance has fully closed.</summary>
/// <remarks>
/// A program can't restart itself while it's still running, so this asks Windows' own PowerShell to wait for the game
/// to exit and then start it again. When Steam launched the game, it's relaunched through Steam, so play time and
/// achievements still count; otherwise SMAPI is started directly.
/// </remarks>
internal static class GameRestarter
{
    /*********
    ** Fields
    *********/
    /// <summary>Stardew Valley's Steam app ID.</summary>
    private const string SteamAppId = "413150";


    /*********
    ** Public methods
    *********/
    /// <summary>Arrange for the game to start again after it closes.</summary>
    /// <returns>Returns whether the restart could be arranged; if not, the player just launches it themselves.</returns>
    public static bool TrySchedule(IMonitor monitor)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            // Steam sets this for the games it launches, including through custom launch options
            bool viaSteam = Environment.GetEnvironmentVariable("SteamAppId") == SteamAppId
                || Environment.GetEnvironmentVariable("SteamGameId") == SteamAppId;

            string start = viaSteam
                ? $"Start-Process '{Quote("steam://rungameid/" + SteamAppId)}'"
                : $"Start-Process -FilePath '{Quote(Path.Combine(Constants.GamePath, "StardewModdingAPI.exe"))}' -WorkingDirectory '{Quote(Constants.GamePath)}'";

            // the short pause lets Steam notice the game has closed, or it would refuse to start it "again"
            string script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; Start-Sleep -Seconds 3; {start}";

            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            monitor.Log($"The game will restart {(viaSteam ? "through Steam" : "through SMAPI")} once it has closed.", LogLevel.Info);
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log($"Couldn't arrange the restart, the player will launch the game themselves: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Escape a value for a single-quoted PowerShell string.</summary>
    private static string Quote(string value)
    {
        return value.Replace("'", "''");
    }
}
