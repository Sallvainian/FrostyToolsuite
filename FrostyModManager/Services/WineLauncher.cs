using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Frosty.Sdk;
using Frosty.Sdk.Utils;
using FrostyModManager.Models;

namespace FrostyModManager.Services;

/// <summary>
/// Gets the game ready to load mod data under Proton and launches it through Steam.
/// </summary>
public static class WineLauncher
{
    private const string c_shimName = "bcrypt.dll";

    /// <summary>
    /// Copies the bcrypt shim into the game folder and makes wine load it instead of its builtin bcrypt, which the
    /// game needs to accept the modified data.
    /// </summary>
    public static bool PrepareGame(GameInstall inGame)
    {
        string shim = Path.Combine(Utils.BaseDirectory, "ThirdParty", c_shimName);
        string target = Path.Combine(inGame.Directory, c_shimName);
        if (!File.Exists(shim))
        {
            FrostyLogger.Logger?.LogError($"Missing {shim}, reinstall the mod manager.");
            return false;
        }

        if (!File.Exists(target) || !SHA256.HashData(File.ReadAllBytes(target)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(shim))))
        {
            File.Copy(shim, target, true);
            FrostyLogger.Logger?.LogInfo($"Copied {c_shimName} into the game folder");
        }

        if (inGame.WinePrefix is null)
        {
            FrostyLogger.Logger?.LogWarning(
                "No Proton prefix found for this game, add WINEDLLOVERRIDES=\"bcrypt=n,b\" to its launch options yourself.");
            return true;
        }

        return EnsureDllOverride(inGame);
    }

    /// <summary>
    /// Adds a per-exe "bcrypt"="native,builtin" override to the prefix registry, the same as winecfg's
    /// Applications tab, so it applies without touching the Steam launch options.
    /// </summary>
    private static bool EnsureDllOverride(GameInstall inGame)
    {
        string userReg = Path.Combine(inGame.WinePrefix!, "user.reg");
        if (!File.Exists(userReg))
        {
            FrostyLogger.Logger?.LogWarning("The game's Proton prefix has no user.reg yet, launch the game once without mods first.");
            return false;
        }

        string section = $"[Software\\\\Wine\\\\AppDefaults\\\\{inGame.ExeName}\\\\DllOverrides]";
        string[] lines = File.ReadAllLines(userReg);
        int index = Array.FindIndex(lines, l => l.StartsWith(section, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            for (int i = index + 1; i < lines.Length && !lines[i].StartsWith('['); i++)
            {
                if (lines[i].StartsWith("\"bcrypt\"=", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // wineserver writes user.reg back when the prefix shuts down, an edit made while it runs gets lost
        if (InitFsKeyFinder.FindGameProcess(inGame.ExeName) is not null)
        {
            FrostyLogger.Logger?.LogError("Close the game first, the bcrypt override can only be added while it is not running.");
            return false;
        }

        File.Copy(userReg, userReg + ".frosty-backup", true);
        if (index >= 0)
        {
            var list = lines.ToList();
            list.Insert(index + 1, "\"bcrypt\"=\"native,builtin\"");
            File.WriteAllLines(userReg, list);
        }
        else
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.AppendAllText(userReg, $"\n{section} {timestamp}\n\"bcrypt\"=\"native,builtin\"\n");
        }

        FrostyLogger.Logger?.LogInfo($"Added the bcrypt dll override for {inGame.ExeName} to the Proton prefix");
        return true;
    }

    public static void Launch(GameInstall inGame)
    {
        if (inGame.SteamAppId is null)
        {
            FrostyLogger.Logger?.LogError("Launching is only supported for Steam games so far, start the game yourself.");
            return;
        }

        // the mods are compiled into the game folder, so a plain launch picks them up
        ProcessStartInfo info = new("steam") { UseShellExecute = false };
        info.ArgumentList.Add("-applaunch");
        info.ArgumentList.Add(inGame.SteamAppId);
        Process.Start(info);
        FrostyLogger.Logger?.LogInfo($"Launching {inGame.DisplayName} through Steam");
    }
}
