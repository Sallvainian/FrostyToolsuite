using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Frosty.Sdk;
using FrostyModManager.Models;

namespace FrostyModManager.Services;

public sealed class InstalledFile
{
    public string Path { get; set; } = string.Empty;
    public bool HadOriginal { get; set; }
}

public sealed class InstallManifest
{
    public string Pack { get; set; } = string.Empty;
    public List<string> Mods { get; set; } = new();
    public List<InstalledFile> Files { get; set; } = new();
}

/// <summary>
/// Puts the generated mod data into the game folder, for games that ignore -dataPath (Dead Space).
/// Originals are moved into FrostyBackup inside the game folder and the modded files are symlinked in their place,
/// so installing and restoring is instant and the generated data stays the single copy.
/// </summary>
public static class GameInstaller
{
    private const string c_backupDirName = "FrostyBackup";
    private const string c_manifestName = "installed.json";

    // bookkeeping files the executor writes into the root of the mod data
    private static readonly HashSet<string> s_executorFiles = new(StringComparer.OrdinalIgnoreCase) { "mods.json", "head.txt" };

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    private static string BackupDir(GameInstall inGame) => Path.Combine(inGame.Directory, c_backupDirName);
    private static string ManifestPath(GameInstall inGame) => Path.Combine(BackupDir(inGame), c_manifestName);

    public static InstallManifest? GetInstalled(GameInstall inGame)
    {
        string path = ManifestPath(inGame);
        return File.Exists(path) ? JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(path)) : null;
    }

    public static void Install(GameInstall inGame, string inModDataDir, string inPack, List<string> inMods)
    {
        if (GetInstalled(inGame) is not null)
        {
            throw new InvalidOperationException("Mods are already installed, restore vanilla first.");
        }

        InstallManifest manifest = new() { Pack = inPack, Mods = inMods };
        Directory.CreateDirectory(BackupDir(inGame));
        try
        {
            foreach (string file in EnumerateModdedFiles(inModDataDir, true))
            {
                string relative = Path.GetRelativePath(inModDataDir, file);
                string target = Path.Combine(inGame.Directory, relative);
                string backup = Path.Combine(BackupDir(inGame), relative);

                FileInfo targetInfo = new(target);
                bool hadOriginal = targetInfo.Exists || targetInfo.LinkTarget is not null;
                if (hadOriginal)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(target, backup, true);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                }

                // record before linking, a crash in between must still restore the original
                manifest.Files.Add(new InstalledFile { Path = relative, HadOriginal = hadOriginal });
                File.CreateSymbolicLink(target, file);
            }
        }
        finally
        {
            File.WriteAllText(ManifestPath(inGame), JsonSerializer.Serialize(manifest, s_options));
        }

        FrostyLogger.Logger?.LogInfo($"Installed {manifest.Files.Count} modded file(s) into the game folder");
    }

    public static void Uninstall(GameInstall inGame)
    {
        InstallManifest? manifest = GetInstalled(inGame);
        if (manifest is null)
        {
            return;
        }

        foreach (InstalledFile file in Enumerable.Reverse(manifest.Files))
        {
            string target = Path.Combine(inGame.Directory, file.Path);
            string backup = Path.Combine(BackupDir(inGame), file.Path);

            FileInfo targetInfo = new(target);
            if (targetInfo.Exists || targetInfo.LinkTarget is not null)
            {
                targetInfo.Delete();
            }

            if (file.HadOriginal && File.Exists(backup))
            {
                File.Move(backup, target);
            }
        }

        File.Delete(ManifestPath(inGame));

        // never delete a backup folder that still holds originals
        if (Directory.EnumerateFiles(BackupDir(inGame), "*", SearchOption.AllDirectories).Any())
        {
            FrostyLogger.Logger?.LogWarning($"Some files are still left in {BackupDir(inGame)}, check them before deleting it.");
        }
        else
        {
            Directory.Delete(BackupDir(inGame), true);
        }

        FrostyLogger.Logger?.LogInfo($"Restored {manifest.Files.Count} vanilla file(s)");
    }

    /// <summary>
    /// The executor symlinks every file it did not change, only the real files are modded.
    /// </summary>
    private static IEnumerable<string> EnumerateModdedFiles(string inDir, bool inIsRoot)
    {
        foreach (string file in Directory.EnumerateFiles(inDir))
        {
            if ((inIsRoot && s_executorFiles.Contains(Path.GetFileName(file))) || new FileInfo(file).LinkTarget is not null)
            {
                continue;
            }

            yield return file;
        }

        foreach (string dir in Directory.EnumerateDirectories(inDir))
        {
            if (new DirectoryInfo(dir).LinkTarget is not null)
            {
                continue;
            }

            foreach (string file in EnumerateModdedFiles(dir, false))
            {
                yield return file;
            }
        }
    }
}
