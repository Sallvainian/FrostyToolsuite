using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Frosty.Sdk.Utils;

/// <summary>
/// Decides where Frosty keeps its writable data (keys, sdks, caches, mods, oodle) and makes the files shipped
/// next to the executable available there. On Linux that is $XDG_DATA_HOME/frosty so a read-only install
/// (/usr/lib, an AppImage) works and every package format shares the same data.
/// </summary>
public static class FrostyDirectories
{
    // read-only files shipped next to the executable that the sdk looks up relative to the data directory
    private static readonly string[] s_shippedDirectories = { "Profiles", "Meta", Path.Combine("Sdk", "Strings"), "ThirdParty" };

    private static bool s_resolverRegistered;

    public static string AppDirectory => Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;

    /// <summary>
    /// Sets <see cref="Utils.BaseDirectory"/> to the data directory and returns it.
    /// </summary>
    public static string Initialize()
    {
        string appDir = AppDirectory;
        string dataDir = GetDataDirectory(appDir);

        Directory.CreateDirectory(dataDir);
        if (!IsSameDirectory(appDir, dataDir))
        {
            foreach (string dir in s_shippedDirectories)
            {
                Mirror(Path.Combine(appDir, dir), Path.Combine(dataDir, dir));
            }
        }

        Utils.BaseDirectory = dataDir;

        if (!s_resolverRegistered)
        {
            // ThirdParty libs (oodle gets downloaded at runtime) live in the data directory, not next to the executable
            NativeLibrary.SetDllImportResolver(typeof(FrostyDirectories).Assembly, ResolveThirdParty);
            s_resolverRegistered = true;
        }

        return dataDir;
    }

    private static string GetDataDirectory(string inAppDir)
    {
        string? overridden = Environment.GetEnvironmentVariable("FROSTY_DATA_DIR");
        if (!string.IsNullOrEmpty(overridden))
        {
            return Path.GetFullPath(overridden);
        }

        if (!OperatingSystem.IsLinux())
        {
            return inAppDir;
        }

        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome) || !Path.IsPathRooted(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(dataHome, "frosty");
    }

    private static void Mirror(string inSource, string inDestination)
    {
        if (!Directory.Exists(inSource))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(inSource, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(inDestination, Path.GetRelativePath(inSource, file));
            FileInfo source = new(file);
            FileInfo existing = new(target);

            if (existing.Exists && existing.Length == source.Length && existing.LastWriteTimeUtc == source.LastWriteTimeUtc)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
            File.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
        }
    }

    private static IntPtr ResolveThirdParty(string inLibraryName, Assembly inAssembly, DllImportSearchPath? inSearchPath)
    {
        if (!inLibraryName.StartsWith("ThirdParty/", StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        string extension = OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";
        foreach (string dir in new[] { Utils.BaseDirectory, AppDirectory })
        {
            if (!string.IsNullOrEmpty(dir) && NativeLibrary.TryLoad(Path.Combine(dir, inLibraryName + extension), out IntPtr handle))
            {
                return handle;
            }
        }

        // fall back to the default probing
        return IntPtr.Zero;
    }

    private static bool IsSameDirectory(string inA, string inB)
    {
        // compare by touching a file, the paths can differ by symlinks (/home -> /var/home)
        string probe = Path.Combine(inB, $".frosty-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            return File.Exists(Path.Combine(inA, Path.GetFileName(probe)));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
