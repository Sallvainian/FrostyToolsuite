using System.IO;

namespace FrostyModManager.Models;

/// <summary>
/// A Frostbite game on disk that has a matching profile.
/// </summary>
public sealed record GameInstall(string DisplayName, string ExePath, string? SteamAppId, string? WinePrefix)
{
    public string Directory => Path.GetDirectoryName(ExePath)!;
    public string ExeName => Path.GetFileName(ExePath);
    public string ProfileName => Path.GetFileNameWithoutExtension(ExePath);

    public override string ToString() => SteamAppId is null ? DisplayName : $"{DisplayName} (Steam)";
}
