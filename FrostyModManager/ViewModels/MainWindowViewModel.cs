using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frosty.ModSupport.Mod;
using Frosty.Sdk;
using Frosty.Sdk.Utils;
using FrostyModManager.Models;
using FrostyModManager.Services;

namespace FrostyModManager.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int c_maxLogLines = 2000;

    public ObservableCollection<GameInstall> Games { get; } = new();
    public ObservableCollection<ModItem> Mods { get; } = new();
    public ObservableCollection<LogLine> Log { get; } = new();

    [ObservableProperty] private GameInstall? m_selectedGame;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveModCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private ModItem? m_selectedMod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(NeedsSetup))]
    [NotifyCanExecuteChangedFor(nameof(SetupCommand), nameof(ApplyCommand), nameof(LaunchModdedCommand),
        nameof(LaunchVanillaCommand), nameof(RemoveModCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private bool m_isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(NeedsSetup))]
    [NotifyCanExecuteChangedFor(nameof(SetupCommand), nameof(ApplyCommand), nameof(LaunchModdedCommand))]
    private SessionState m_state = SessionState.NotLoaded;

    [ObservableProperty] private string m_status = "Starting…";

    public bool IsReady => State == SessionState.Ready && !IsBusy;
    public bool NeedsSetup => State == SessionState.NeedsSetup;

    private readonly Settings m_settings;
    private readonly Dictionary<string, string> m_profileNames;
    private GameSession? m_session;

    public MainWindowViewModel()
    {
        Utils.BaseDirectory = Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty;
        FrostyLogger.Logger = new UiLogger(line => Dispatcher.UIThread.Post(() => AddLog(line)));

        m_settings = Settings.Load();
        m_profileNames = SteamLibrary.LoadProfileNames(Path.Combine(Utils.BaseDirectory, "Profiles"));

        foreach (GameInstall game in SteamLibrary.FindGames(m_profileNames))
        {
            Games.Add(game);
        }

        if (m_settings.LastGameExe is not null && Games.All(g => g.ExePath != m_settings.LastGameExe) &&
            File.Exists(m_settings.LastGameExe))
        {
            Games.Add(SteamLibrary.FromExe(m_settings.LastGameExe, m_profileNames));
        }

        m_selectedGame = Games.FirstOrDefault(g => g.ExePath == m_settings.LastGameExe) ?? Games.FirstOrDefault();
        if (m_selectedGame is null)
        {
            Status = "No supported game found, use Browse to pick the game's exe.";
        }
        else
        {
            _ = LoadGameAsync(m_selectedGame);
        }
    }

    partial void OnSelectedGameChanged(GameInstall? value)
    {
        if (value is null || value == m_session?.Game)
        {
            return;
        }

        if (m_session is not null)
        {
            // the sdk managers are static, they can't be pointed at a second game
            m_settings.LastGameExe = value.ExePath;
            m_settings.Save();
            Status = $"Restart the mod manager to switch to {value.DisplayName}.";
            AddLog(new LogLine(Status, LogLine.WarningBrush));
            return;
        }

        _ = LoadGameAsync(value);
    }

    public void AddGame(string inExePath)
    {
        GameInstall? game = Games.FirstOrDefault(g => g.ExePath == inExePath);
        if (game is null)
        {
            game = SteamLibrary.FromExe(inExePath, m_profileNames);
            Games.Add(game);
        }

        SelectedGame = game;
    }

    private async Task LoadGameAsync(GameInstall inGame)
    {
        m_session = new GameSession(inGame);
        m_settings.LastGameExe = inGame.ExePath;
        m_settings.Save();

        Status = $"Loading {inGame.DisplayName}…";
        await RunBusyAsync(() => m_session.Load());
        OnSessionChanged();
    }

    private void OnSessionChanged()
    {
        if (m_session is null)
        {
            return;
        }

        State = m_session.State;
        Status = m_session.State switch
        {
            SessionState.Ready => $"{m_session.Game.DisplayName} ready · {Mods.Count(m => m.IsEnabled)} of {Mods.Count} mods enabled",
            SessionState.NeedsSetup => "First-time setup: start the game, wait for the main menu, then click “Set up from running game”.",
            _ => m_session.StateMessage
        };

        if (m_session.State == SessionState.Ready && Mods.Count == 0)
        {
            LoadModList();
        }
    }

    private void LoadModList()
    {
        if (m_session is null || !m_settings.ModLists.TryGetValue(m_session.Game.ExePath, out List<ModListEntry>? entries))
        {
            return;
        }

        foreach (ModListEntry entry in entries)
        {
            FrostyModDetails? details = File.Exists(entry.File) ? GameSession.GetDetails(entry.File) : null;
            if (details is null)
            {
                AddLog(new LogLine($"Dropped {Path.GetFileName(entry.File)} from the list, the file is missing or unreadable.", LogLine.WarningBrush));
                continue;
            }

            Mods.Add(new ModItem(entry.File, details, entry.Enabled));
        }

        foreach (ModItem mod in Mods)
        {
            mod.PropertyChanged += (_, _) => SaveModList();
        }
    }

    private void SaveModList()
    {
        if (m_session is null)
        {
            return;
        }

        m_settings.ModLists[m_session.Game.ExePath] =
            Mods.Select(m => new ModListEntry { File = m.FilePath, Enabled = m.IsEnabled }).ToList();
        m_settings.Save();
        OnSessionChanged();
    }

    public async Task ImportAsync(IEnumerable<string> inPaths)
    {
        if (m_session is null || State != SessionState.Ready || IsBusy)
        {
            AddLog(new LogLine("The game has to finish loading before mods can be added.", LogLine.WarningBrush));
            return;
        }

        List<string> paths = inPaths.ToList();
        List<string> imported = new();
        await RunBusyAsync(() =>
        {
            foreach (string path in paths)
            {
                imported.AddRange(m_session.Import(path));
            }
        });

        foreach (string file in imported)
        {
            // re-importing a file replaces the old entry but keeps its position
            int existing = Mods.ToList().FindIndex(m => m.FilePath == file);
            ModItem item = new(file, GameSession.GetDetails(file)!, true);
            item.PropertyChanged += (_, _) => SaveModList();
            if (existing >= 0)
            {
                Mods[existing] = item;
            }
            else
            {
                Mods.Add(item);
            }

            SelectedMod = item;
        }

        SaveModList();
    }

    private bool CanEditSelected() => SelectedMod is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void RemoveMod()
    {
        if (SelectedMod is null)
        {
            return;
        }

        int index = Mods.IndexOf(SelectedMod);
        Mods.Remove(SelectedMod);
        SelectedMod = Mods.Count == 0 ? null : Mods[Math.Min(index, Mods.Count - 1)];
        SaveModList();
    }

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanEditSelected))]
    private void MoveDown() => Move(1);

    private void Move(int inOffset)
    {
        if (SelectedMod is null)
        {
            return;
        }

        int index = Mods.IndexOf(SelectedMod);
        int target = index + inOffset;
        if (target < 0 || target >= Mods.Count)
        {
            return;
        }

        ModItem item = SelectedMod;
        Mods.Move(index, target);
        SelectedMod = item;
        SaveModList();
    }

    private bool CanSetup() => !IsBusy && State == SessionState.NeedsSetup;

    [RelayCommand(CanExecute = nameof(CanSetup))]
    private async Task SetupAsync()
    {
        if (m_session is null)
        {
            return;
        }

        Status = "Reading the key and types from the running game…";
        await RunBusyAsync(() => m_session.SetupFromRunningGame());
        OnSessionChanged();
    }

    private bool CanApply() => !IsBusy && State == SessionState.Ready;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync() => await ApplyInternalAsync();

    private async Task<bool> ApplyInternalAsync()
    {
        if (m_session is null)
        {
            return false;
        }

        List<string> enabled = Mods.Where(m => m.IsEnabled).Select(m => m.FilePath).ToList();
        AddLog(new LogLine($"Applying {enabled.Count} mod(s)…", LogLine.InfoBrush));
        Status = "Applying mods…";

        bool success = false;
        await RunBusyAsync(() => success = m_session.Apply(enabled) && WineLauncher.PrepareGame(m_session.Game));
        OnSessionChanged();
        return success;
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task LaunchModdedAsync()
    {
        if (m_session is not null && await ApplyInternalAsync())
        {
            WineLauncher.Launch(m_session.Game, true);
        }
    }

    private bool CanLaunchVanilla() => !IsBusy && SelectedGame is not null;

    [RelayCommand(CanExecute = nameof(CanLaunchVanilla))]
    private void LaunchVanilla()
    {
        if (SelectedGame is not null)
        {
            WineLauncher.Launch(SelectedGame, false);
        }
    }

    [RelayCommand]
    private void ClearLog() => Log.Clear();

    private async Task RunBusyAsync(Action inAction)
    {
        IsBusy = true;
        try
        {
            await Task.Run(inAction);
        }
        catch (Exception e)
        {
            AddLog(new LogLine($"Unexpected error: {e}", LogLine.ErrorBrush));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddLog(LogLine inLine)
    {
        Log.Add(inLine);
        if (Log.Count > c_maxLogLines)
        {
            Log.RemoveAt(0);
        }
    }
}
