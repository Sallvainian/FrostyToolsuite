using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FrostyModManager.Models;

namespace FrostyModManager.Services;

/// <summary>
/// Finds installed Frostbite games in the Steam libraries and the Proton prefix they run in.
/// </summary>
public static partial class SteamLibrary
{
    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex("\"(appid|installdir)\"\\s+\"([^\"]+)\"")]
    private static partial Regex ManifestFieldRegex();

    /// <summary>
    /// Reads the exe name and display name out of every profile.
    /// </summary>
    public static Dictionary<string, string> LoadProfileNames(string inProfilesDir)
    {
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(inProfilesDir))
        {
            return names;
        }

        foreach (string file in Directory.EnumerateFiles(inProfilesDir, "*.json"))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                string? name = doc.RootElement.GetProperty("Name").GetString();
                string? display = doc.RootElement.TryGetProperty("DisplayName", out JsonElement d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    names[name] = display ?? name;
                }
            }
            catch (Exception)
            {
                // a broken profile just doesn't get detected
            }
        }

        return names;
    }

    public static List<GameInstall> FindGames(IReadOnlyDictionary<string, string> inProfileNames)
    {
        List<GameInstall> games = new();
        foreach (string library in GetLibraryPaths())
        {
            foreach ((string appId, string installDir) in GetInstalledApps(library))
            {
                string dir = Path.Combine(library, "steamapps", "common", installDir);
                foreach ((string exeName, string displayName) in inProfileNames)
                {
                    string exe = Path.Combine(dir, exeName + ".exe");
                    if (File.Exists(exe))
                    {
                        games.Add(new GameInstall(displayName, exe, appId, GetPrefix(library, appId)));
                    }
                }
            }
        }

        return games;
    }

    /// <summary>
    /// Builds a <see cref="GameInstall"/> for an exe picked by hand, looking up its Steam app if it has one.
    /// </summary>
    public static GameInstall FromExe(string inExePath, IReadOnlyDictionary<string, string> inProfileNames)
    {
        string name = Path.GetFileNameWithoutExtension(inExePath);
        string display = inProfileNames.TryGetValue(name, out string? d) ? d : name;
        string dir = Path.GetFullPath(Path.GetDirectoryName(inExePath)!);

        foreach (string library in GetLibraryPaths())
        {
            foreach ((string appId, string installDir) in GetInstalledApps(library))
            {
                if (Path.GetFullPath(Path.Combine(library, "steamapps", "common", installDir)) == dir)
                {
                    return new GameInstall(display, inExePath, appId, GetPrefix(library, appId));
                }
            }
        }

        return new GameInstall(display, inExePath, null, null);
    }

    private static string? GetPrefix(string inLibrary, string inAppId)
    {
        string prefix = Path.Combine(inLibrary, "steamapps", "compatdata", inAppId, "pfx");
        return Directory.Exists(prefix) ? prefix : null;
    }

    private static IEnumerable<string> GetLibraryPaths()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots =
        {
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
        };

        HashSet<string> seen = new();
        foreach (string root in roots)
        {
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            IEnumerable<string> paths = LibraryPathRegex().Matches(File.ReadAllText(vdf))
                .Select(m => m.Groups[1].Value.Replace("\\\\", "\\")).Prepend(root);
            foreach (string path in paths)
            {
                // ~/.steam/steam is usually a symlink to ~/.local/share/Steam
                string real = new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(path);
                if (Directory.Exists(Path.Combine(real, "steamapps")) && seen.Add(real.TrimEnd('/')))
                {
                    yield return real;
                }
            }
        }
    }

    private static IEnumerable<(string AppId, string InstallDir)> GetInstalledApps(string inLibrary)
    {
        foreach (string manifest in Directory.EnumerateFiles(Path.Combine(inLibrary, "steamapps"), "appmanifest_*.acf"))
        {
            string? appId = null, installDir = null;
            foreach (Match match in ManifestFieldRegex().Matches(File.ReadAllText(manifest)))
            {
                if (match.Groups[1].Value == "appid")
                {
                    appId ??= match.Groups[2].Value;
                }
                else
                {
                    installDir ??= match.Groups[2].Value;
                }
            }

            if (appId is not null && installDir is not null)
            {
                yield return (appId, installDir);
            }
        }
    }
}
