using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Frosty.Sdk.Utils;

namespace FrostyModManager.Services;

public sealed class ModListEntry
{
    public string File { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Persists the selected game and the mod list per game next to the executable.
/// </summary>
public sealed class Settings
{
    public string? LastGameExe { get; set; }

    /// <summary>
    /// Mod list per game exe path, in load order.
    /// </summary>
    public Dictionary<string, List<ModListEntry>> ModLists { get; set; } = new();

    private static string FilePath => Path.Combine(Utils.BaseDirectory, "ModManager.json");

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
        }
        catch (Exception)
        {
            // fall through to defaults, a corrupt file shouldn't stop the app from starting
        }

        return new Settings();
    }

    public void Save()
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, s_options));
    }
}
