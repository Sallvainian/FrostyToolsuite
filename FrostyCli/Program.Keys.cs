using System;
using System.IO;
using Frosty.Sdk;
using Frosty.Sdk.Utils;
using Sharprompt;

namespace FrostyCli;

internal static partial class Program
{
    private static void FindInitFsKey(FileInfo? inGameFileInfo, int? inPid, FileInfo? inOutputFileInfo)
    {
        FileInfo? game = inGameFileInfo ?? RequestFile("Input the path to the games executable");
        if (game?.Exists != true || game.DirectoryName is null)
        {
            Logger.LogErrorInternal("Game does not exist.");
            return;
        }

        // the profile decides how the initfs file is deobfuscated
        FrostyLogger.Logger = new Logger();
        FrostyDirectories.Initialize();
        if (!ProfilesLibrary.Initialize(Path.GetFileNameWithoutExtension(game.Name)))
        {
            return;
        }

        byte[]? encrypted = InitFsKeyFinder.GetEncryptedInitFs(game.DirectoryName);
        if (encrypted is null)
        {
            Logger.LogErrorInternal("Could not find an encrypted initfs_Win32 in the games Patch or Data folder.");
            return;
        }

        int pid = inPid ?? InitFsKeyFinder.FindGameProcess(game.Name) ??
            Prompt.Input<int>("Input pid of the currently running game");

        byte[]? key = InitFsKeyFinder.FindKey(encrypted, pid, game.Name);
        if (key is null)
        {
            Logger.LogErrorInternal(
                "No initfs key found. Make sure the game is running and has reached the main menu, then try again.");
            return;
        }

        string output = inOutputFileInfo?.FullName ?? Path.Combine(Directory.GetCurrentDirectory(), "initfs.key");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, key);
        Logger.LogInfoInternal($"Found initfs key, saved to {output}");
    }
}
