using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Frosty.ModSupport;
using Frosty.ModSupport.Mod;
using Frosty.Sdk;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Sdk;
using Frosty.Sdk.Utils;
using FrostyModManager.Models;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace FrostyModManager.Services;

public enum SessionState
{
    NotLoaded,
    NeedsSetup,
    Ready,
    Failed
}

/// <summary>
/// Wraps the sdk managers for one game. The managers are static, so a process can only ever load one game.
/// All methods block, call them off the ui thread.
/// </summary>
public sealed class GameSession
{
    public GameInstall Game { get; }
    public SessionState State { get; private set; } = SessionState.NotLoaded;
    public string StateMessage { get; private set; } = string.Empty;

    public string ModsDirectory => Path.Combine(Utils.BaseDirectory, "Mods", ProfilesLibrary.InternalName);
    public string ModDataDirectory => Path.Combine(Game.Directory, "ModData", "Default");

    private string KeyPath => Path.Combine(Utils.BaseDirectory, "Keys", $"{ProfilesLibrary.InternalName}.initfs.key");

    public GameSession(GameInstall inGame)
    {
        Game = inGame;
    }

    /// <summary>
    /// Loads the game data, stops at <see cref="SessionState.NeedsSetup"/> if the key or sdk still has to be pulled
    /// out of the running game.
    /// </summary>
    public SessionState Load()
    {
        // compiled mods live in the game folder, take them out while loading so the managers only ever see vanilla
        InstallManifest? installed = GameInstaller.GetInstalled(Game);
        if (installed is null)
        {
            return LoadVanilla();
        }

        GameInstaller.Uninstall(Game);
        try
        {
            return LoadVanilla();
        }
        finally
        {
            if (Directory.Exists(ModDataDirectory))
            {
                GameInstaller.Install(Game, ModDataDirectory, installed.Pack, installed.Mods);
            }
            else
            {
                FrostyLogger.Logger?.LogWarning("The compiled mod data is gone, the game is vanilla now. Compile again to reinstall the mods.");
            }
        }
    }

    private SessionState LoadVanilla()
    {
        if (!ProfilesLibrary.Initialize(Game.ProfileName))
        {
            return Fail($"No profile for {Game.ExeName}, this game is not supported.");
        }

        if (ProfilesLibrary.RequiresBundleKey || ProfilesLibrary.RequiresCasKey)
        {
            return Fail("This game needs bundle/cas keys, which the mod manager does not handle yet. Use FrostyCli.");
        }

        if (ProfilesLibrary.RequiresInitFsKey && !KeyManager.HasKey("InitFsKey"))
        {
            if (!File.Exists(KeyPath))
            {
                return NeedsSetup("The initfs key has not been recovered yet.");
            }

            KeyManager.AddKey("InitFsKey", File.ReadAllBytes(KeyPath));
        }

        if (!FileSystemManager.IsInitialized && !FileSystemManager.Initialize(Game.Directory))
        {
            return Fail("Failed to read the game's file system (layout.toc / initfs).");
        }

        if (!File.Exists(ProfilesLibrary.SdkPath))
        {
            return NeedsSetup("The type sdk has not been generated yet.");
        }

        if (!TypeLibrary.IsInitialized && !TypeLibrary.Initialize())
        {
            return Fail("Failed to load the type sdk.");
        }

        if (!ResourceManager.Initialize() || !AssetManager.Initialize())
        {
            return Fail("Failed to load the game's assets.");
        }

        State = SessionState.Ready;
        StateMessage = $"{ProfilesLibrary.DisplayName} loaded";
        return State;
    }

    /// <summary>
    /// Pulls the initfs key and the type sdk out of the running game, then loads it.
    /// </summary>
    public SessionState SetupFromRunningGame()
    {
        if (!ProfilesLibrary.Initialize(Game.ProfileName))
        {
            return Fail($"No profile for {Game.ExeName}, this game is not supported.");
        }

        int? pid = InitFsKeyFinder.FindGameProcess(Game.ExeName);
        if (pid is null)
        {
            return NeedsSetup($"{Game.ExeName} is not running. Start the game, wait for the main menu, then run setup again.");
        }

        if (ProfilesLibrary.RequiresInitFsKey && !File.Exists(KeyPath))
        {
            byte[]? encrypted = InitFsKeyFinder.GetEncryptedInitFs(Game.Directory);
            if (encrypted is null)
            {
                return Fail("Could not find an encrypted initfs_Win32 in the game folder.");
            }

            byte[]? key = InitFsKeyFinder.FindKey(encrypted, pid.Value, Game.ExeName);
            if (key is null)
            {
                return NeedsSetup("The initfs key was not found in memory. Wait until the game reaches the main menu and try again.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            File.WriteAllBytes(KeyPath, key);
            FrostyLogger.Logger?.LogInfo($"Saved initfs key to {KeyPath}");
        }

        // load up to the point where the sdk is needed, so the key gets checked before dumping types
        SessionState state = Load();
        if (state != SessionState.NeedsSetup || File.Exists(ProfilesLibrary.SdkPath))
        {
            return state;
        }

        TypeSdkGenerator generator = new();
        using (Process process = Process.GetProcessById(pid.Value))
        {
            if (!generator.DumpTypes(process))
            {
                return Fail("Failed to dump the types from the game's memory.");
            }
        }

        FrostyLogger.Logger?.LogInfo("Types dumped, the game can be closed now.");
        if (!generator.CreateSdk(ProfilesLibrary.SdkPath))
        {
            return Fail("Failed to compile the type sdk.");
        }

        return Load();
    }

    /// <summary>
    /// Copies a mod into the managed mods folder, converting Frosty 1.x mods to the current format.
    /// Archives (.zip, .rar, .7z) get searched for mods, Nexus ships most of them that way.
    /// </summary>
    /// <returns>The paths of the imported mods.</returns>
    public List<string> Import(string inPath)
    {
        List<string> imported = new();
        Directory.CreateDirectory(ModsDirectory);

        if (IsArchive(inPath))
        {
            string temp = Directory.CreateTempSubdirectory("frosty-import-").FullName;
            try
            {
                using IArchive archive = ArchiveFactory.Open(inPath);
                foreach (IArchiveEntry entry in archive.Entries.Where(e => !e.IsDirectory && IsModFile(e.Key ?? string.Empty)))
                {
                    // flatten, only the file name matters and it keeps entries from escaping the temp dir
                    string extracted = Path.Combine(temp, Path.GetFileName(entry.Key!));
                    entry.WriteToFile(extracted, new ExtractionOptions { Overwrite = true });
                    imported.AddRange(Import(extracted));
                }

                if (imported.Count == 0)
                {
                    FrostyLogger.Logger?.LogWarning($"{Path.GetFileName(inPath)} does not contain any .fbmod or .fbcollection files.");
                }
            }
            finally
            {
                Directory.Delete(temp, true);
            }

            return imported;
        }

        if (!IsModFile(inPath))
        {
            FrostyLogger.Logger?.LogError($"{Path.GetFileName(inPath)} is not a .fbmod, .fbcollection or an archive (.zip, .rar, .7z).");
            return imported;
        }

        string destination = Path.Combine(ModsDirectory, Path.GetFileName(inPath));
        if (GetDetails(inPath) is not null)
        {
            if (Path.GetFullPath(inPath) != Path.GetFullPath(destination))
            {
                File.Copy(inPath, destination, true);
            }
        }
        else if (Path.GetExtension(inPath).Equals(".fbmod", StringComparison.OrdinalIgnoreCase))
        {
            // frosty 1.x mods need to be rewritten against the loaded game data
            FrostyLogger.Logger?.LogInfo($"Converting {Path.GetFileName(inPath)} from the Frosty 1.x format");
            if (!ModUpdater.UpdateMod(inPath, destination) || GetDetails(destination) is null)
            {
                FrostyLogger.Logger?.LogError($"Could not convert {Path.GetFileName(inPath)}, it might be for another game.");
                File.Delete(destination);
                return imported;
            }
        }
        else
        {
            FrostyLogger.Logger?.LogError($"{Path.GetFileName(inPath)} is not a collection for {ProfilesLibrary.DisplayName}.");
            return imported;
        }

        FrostyLogger.Logger?.LogInfo($"Added {Path.GetFileName(destination)}");
        imported.Add(destination);
        return imported;
    }

    public static FrostyModDetails? GetDetails(string inPath)
    {
        try
        {
            return Path.GetExtension(inPath).ToLowerInvariant() switch
            {
                ".fbmod" => FrostyMod.GetModDetails(inPath),
                ".fbcollection" => FrostyModCollection.GetModDetails(inPath),
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Generates the ModData folder the game gets pointed at with -dataPath.
    /// </summary>
    public bool Apply(IEnumerable<string> inModPaths)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        FrostyModExecutor executor = new();
        Errors error = executor.GenerateMods(ModDataDirectory, inModPaths.ToList());
        switch (error)
        {
            case Errors.Success:
                FrostyLogger.Logger?.LogInfo($"Generated mod data in {stopwatch.Elapsed.TotalSeconds:F1}s");
                return true;
            case Errors.NoUpdateNeeded:
                FrostyLogger.Logger?.LogInfo("Mod data is already up to date");
                return true;
            default:
                FrostyLogger.Logger?.LogError($"Failed to generate mod data: {error}");
                return false;
        }
    }

    private static bool IsArchive(string inPath)
    {
        string extension = Path.GetExtension(inPath).ToLowerInvariant();
        return extension is ".zip" or ".rar" or ".7z";
    }

    private static bool IsModFile(string inPath)
    {
        string extension = Path.GetExtension(inPath).ToLowerInvariant();
        return extension is ".fbmod" or ".fbcollection";
    }

    private SessionState Fail(string inMessage)
    {
        FrostyLogger.Logger?.LogError(inMessage);
        State = SessionState.Failed;
        StateMessage = inMessage;
        return State;
    }

    private SessionState NeedsSetup(string inMessage)
    {
        FrostyLogger.Logger?.LogWarning(inMessage);
        State = SessionState.NeedsSetup;
        StateMessage = inMessage;
        return State;
    }
}
