using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Frosty.Sdk.Utils;

namespace FrostyModManager.Services;

public sealed class ModListEntry
{
    /// <summary>
    /// File name inside the mods folder.
    /// </summary>
    public string File { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class GameSettings
{
    public string SelectedPack { get; set; } = Settings.DefaultPack;

    /// <summary>
    /// Applied mods per pack, in load order (the last one wins conflicts).
    /// </summary>
    public Dictionary<string, List<ModListEntry>> Packs { get; set; } = new() { [Settings.DefaultPack] = new() };
}

/// <summary>
/// Persists the selected game and the packs per game next to the executable.
/// </summary>
public sealed class Settings
{
    public const string DefaultPack = "Default";

    public string? LastGameExe { get; set; }

    public Dictionary<string, GameSettings> Games { get; set; } = new();

    private static string FilePath => Path.Combine(Utils.BaseDirectory, "ModManager.json");

    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public GameSettings ForGame(string inExePath)
    {
        if (!Games.TryGetValue(inExePath, out GameSettings? settings))
        {
            settings = new GameSettings();
            Games[inExePath] = settings;
        }

        if (!settings.Packs.ContainsKey(DefaultPack))
        {
            settings.Packs[DefaultPack] = new List<ModListEntry>();
        }

        return settings;
    }

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
