using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Frosty.Sdk;
using Frosty.Sdk.Utils;
using FrostyModManager.Models;
using FrostyModManager.Services;

namespace FrostyModManager.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const int c_maxLogLines = 2000;

    public ObservableCollection<GameInstall> Games { get; } = new();
    public ObservableCollection<string> Packs { get; } = new();
    public ObservableCollection<ModItem> AvailableMods { get; } = new();
    public ObservableCollection<AppliedMod> AppliedMods { get; } = new();
    public ObservableCollection<LogLine> Log { get; } = new();

    /// <summary>
    /// The library as shown: filtered mods with a header row per category.
    /// </summary>
    public ObservableCollection<object> LibraryRows { get; } = new();

    /// <summary>
    /// Asks the user a yes/no question, set by the view.
    /// </summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (_, _) => Task.FromResult(true);

    /// <summary>
    /// Asks the user for a line of text, set by the view.
    /// </summary>
    public Func<string, string, Task<string?>> PromptAsync { get; set; } = (_, _) => Task.FromResult<string?>(null);

    [ObservableProperty] private GameInstall? m_selectedGame;
    [ObservableProperty] private string? m_selectedPack;
    [ObservableProperty] private ModItem? m_describedMod;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAppliedCommand), nameof(MoveAppliedUpCommand), nameof(MoveAppliedDownCommand))]
    private AppliedMod? m_selectedApplied;

    [ObservableProperty] private string m_filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyCanExecuteChangedFor(nameof(SetupCommand), nameof(CompileCommand), nameof(RestoreVanillaCommand),
        nameof(LaunchCommand), nameof(AddPackCommand), nameof(RemovePackCommand), nameof(RemoveAppliedCommand),
        nameof(MoveAppliedUpCommand), nameof(MoveAppliedDownCommand), nameof(ClearAppliedCommand))]
    private bool m_isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(NeedsSetup))]
    [NotifyCanExecuteChangedFor(nameof(SetupCommand), nameof(CompileCommand), nameof(RestoreVanillaCommand),
        nameof(AddPackCommand), nameof(RemovePackCommand), nameof(ClearAppliedCommand))]
    private SessionState m_state = SessionState.NotLoaded;

    [ObservableProperty] private string m_status = "Starting…";
    [ObservableProperty] private string m_installedStatus = string.Empty;
    [ObservableProperty] private bool m_isModded;
    [ObservableProperty] private Bitmap? m_heroImage;
    [ObservableProperty] private Bitmap? m_logoImage;

    /// <summary>
    /// Selected tab of the library card, 0 = mods, 1 = log.
    /// </summary>
    [ObservableProperty] private int m_libraryTab;

    /// <summary>
    /// Selected tab of the right card, 0 = load order, 1 = details.
    /// </summary>
    [ObservableProperty] private int m_detailsTab;

    public bool IsReady => State == SessionState.Ready && !IsBusy;
    public bool NeedsSetup => State == SessionState.NeedsSetup;

    public string WindowTitle => SelectedGame is null
        ? "Frosty Mod Manager - 2.0 (Linux)"
        : $"Frosty Mod Manager - 2.0 (Linux) ({SelectedGame.DisplayName})";

    public string AvailableHeader => $"Mods  {AvailableMods.Count}";
    public string AppliedHeader => $"Load order  {AppliedMods.Count}";
    public string AvailableFooter => AvailableMods.Count == 1 ? "1 mod in library" : $"{AvailableMods.Count} mods in library";
    public string GameTitle => SelectedGame?.DisplayName ?? "No game selected";
    public bool HasLogo => LogoImage is not null;

    private readonly Settings m_settings;
    private readonly Dictionary<string, string> m_profileNames;
    private GameSession? m_session;
    private bool m_isLoadingPack;

    private GameSettings? GameSettings => m_session is null ? null : m_settings.ForGame(m_session.Game.ExePath);

    public MainWindowViewModel()
    {
        FrostyDirectories.Initialize();
        FrostyLogger.Logger = new UiLogger(line => Dispatcher.UIThread.Post(() => AddLog(line)));

        AvailableMods.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AvailableHeader));
            OnPropertyChanged(nameof(AvailableFooter));
            RebuildLibrary();
        };
        AppliedMods.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AppliedHeader));
            for (int i = 0; i < AppliedMods.Count; i++)
            {
                AppliedMods[i].Order = i + 1;
            }

            RefreshAppliedFlags();
            SavePack();
        };

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
            Status = "No supported game found, use Tools → Select game executable…";
        }
        else
        {
            _ = LoadGameAsync(m_selectedGame);
        }
    }

    #region Game

    partial void OnSelectedGameChanged(GameInstall? value)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(GameTitle));
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

        HeroImage = LoadArtwork(inGame, "library_hero.jpg");
        LogoImage = LoadArtwork(inGame, "logo.png");
        OnPropertyChanged(nameof(HasLogo));

        Status = $"Loading {inGame.DisplayName}…";
        await RunBusyAsync(() => m_session.Load());
        await OnSessionChangedAsync();
    }

    private static Bitmap? LoadArtwork(GameInstall inGame, string inFileName)
    {
        string? path = SteamLibrary.FindArtwork(inGame, inFileName);
        try
        {
            return path is null ? null : new Bitmap(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task OnSessionChangedAsync()
    {
        if (m_session is null)
        {
            return;
        }

        State = m_session.State;
        UpdateInstalledStatus();
        switch (m_session.State)
        {
            case SessionState.Ready:
                Status = "Ready";
                if (AvailableMods.Count == 0)
                {
                    await LoadModsAsync();
                }

                break;
            case SessionState.NeedsSetup:
                Status = "First-time setup: start the game, wait for the main menu, then use Tools → Set up from running game.";
                break;
            default:
                Status = m_session.StateMessage;
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSetup))]
    private async Task SetupAsync()
    {
        if (m_session is null)
        {
            return;
        }

        Status = "Reading the key and types from the running game…";
        await RunBusyAsync(() => m_session.SetupFromRunningGame());
        await OnSessionChangedAsync();
    }

    private bool CanSetup() => !IsBusy && State == SessionState.NeedsSetup;

    #endregion

    #region Available mods

    private async Task LoadModsAsync()
    {
        if (m_session is null)
        {
            return;
        }

        string dir = m_session.ModsDirectory;
        List<ModItem> mods = new();
        await RunBusyAsync(() =>
        {
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(dir).Order())
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".fbmod" or ".fbcollection"))
                {
                    continue;
                }

                ModItem? mod = ModItem.TryLoad(file);
                if (mod is null)
                {
                    FrostyLogger.Logger?.LogWarning($"Skipping {Path.GetFileName(file)}, it is not a mod for this game.");
                    continue;
                }

                mods.Add(mod);
            }
        });

        foreach (ModItem mod in mods)
        {
            AvailableMods.Add(mod);
        }

        LoadPacks();
    }

    public async Task ImportAsync(IEnumerable<string> inPaths)
    {
        if (m_session is null || State != SessionState.Ready || IsBusy)
        {
            AddLog(new LogLine("The game has to finish loading before mods can be added.", LogLine.WarningBrush));
            return;
        }

        List<string> paths = inPaths.ToList();
        List<ModItem> imported = new();
        await RunBusyAsync(() =>
        {
            foreach (string path in paths)
            {
                foreach (string file in m_session.Import(path))
                {
                    if (ModItem.TryLoad(file) is { } mod)
                    {
                        imported.Add(mod);
                    }
                }
            }
        });

        foreach (ModItem mod in imported)
        {
            // re-adding a file replaces the old entry, also inside the applied list
            int index = AvailableMods.ToList().FindIndex(m => m.FilePath == mod.FilePath);
            if (index >= 0)
            {
                ModItem old = AvailableMods[index];
                AvailableMods[index] = mod;
                for (int i = 0; i < AppliedMods.Count; i++)
                {
                    if (AppliedMods[i].Mod == old)
                    {
                        AppliedMods[i] = new AppliedMod(mod, AppliedMods[i].IsEnabled);
                    }
                }
            }
            else
            {
                AvailableMods.Add(mod);
            }

            DescribedMod = mod;
        }
    }

    public async Task RemoveModsAsync(IReadOnlyList<ModItem> inMods)
    {
        if (inMods.Count == 0 || IsBusy)
        {
            return;
        }

        string what = inMods.Count == 1 ? inMods[0].Title : $"{inMods.Count} mods";
        if (!await ConfirmAsync("Remove Mod(s)", $"Delete {what} from the mods folder? Compiled mods stay installed until the next compile or restore."))
        {
            return;
        }

        foreach (ModItem mod in inMods)
        {
            foreach (AppliedMod applied in AppliedMods.Where(a => a.Mod == mod).ToList())
            {
                AppliedMods.Remove(applied);
            }

            if (GameSettings is { } settings)
            {
                foreach (List<ModListEntry> pack in settings.Packs.Values)
                {
                    pack.RemoveAll(e => Path.GetFileName(e.File) == mod.FileName);
                }
            }

            AvailableMods.Remove(mod);
            File.Delete(mod.FilePath);
            AddLog(new LogLine($"Removed {mod.FileName}", LogLine.InfoBrush));
        }

        m_settings.Save();
        if (DescribedMod is not null && inMods.Contains(DescribedMod))
        {
            DescribedMod = null;
        }
    }

    public void ApplyMods(IEnumerable<ModItem> inMods)
    {
        foreach (ModItem mod in inMods)
        {
            if (AppliedMods.All(a => a.Mod != mod))
            {
                AppliedMods.Add(new AppliedMod(mod, true));
            }
        }
    }

    partial void OnFilterTextChanged(string value) => RebuildLibrary();

    partial void OnDescribedModChanged(ModItem? value)
    {
        if (value is not null)
        {
            DetailsTab = 1;
        }
    }

    private void RebuildLibrary()
    {
        LibraryRows.Clear();
        foreach (IGrouping<string, ModItem> group in AvailableMods.Where(FilterMod)
                     .GroupBy(m => m.Category).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            LibraryRows.Add(new CategoryHeader(group.Key, group.Count()));
            foreach (ModItem mod in group.OrderBy(m => m.Title, StringComparer.OrdinalIgnoreCase))
            {
                LibraryRows.Add(mod);
            }
        }
    }

    [RelayCommand]
    private void ApplyOne(ModItem? inMod)
    {
        if (inMod is not null)
        {
            ApplyMods(new[] { inMod });
            DetailsTab = 0;
        }
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    private bool FilterMod(ModItem mod)
    {
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return mod.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
               mod.Author.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
               mod.Category.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshAppliedFlags()
    {
        HashSet<ModItem> applied = AppliedMods.Select(a => a.Mod).ToHashSet();
        foreach (ModItem mod in AvailableMods)
        {
            mod.IsApplied = applied.Contains(mod);
        }
    }

    #endregion

    #region Packs and applied mods

    private void LoadPacks()
    {
        if (GameSettings is not { } settings)
        {
            return;
        }

        Packs.Clear();
        foreach (string pack in settings.Packs.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            Packs.Add(pack);
        }

        SelectedPack = Packs.Contains(settings.SelectedPack) ? settings.SelectedPack : Settings.DefaultPack;
        LoadAppliedMods();
    }

    partial void OnSelectedPackChanged(string? value)
    {
        if (value is null || GameSettings is not { } settings || m_isLoadingPack)
        {
            return;
        }

        settings.SelectedPack = value;
        m_settings.Save();
        LoadAppliedMods();
        RemovePackCommand.NotifyCanExecuteChanged();
    }

    private void LoadAppliedMods()
    {
        if (GameSettings is not { } settings || SelectedPack is null)
        {
            return;
        }

        m_isLoadingPack = true;
        try
        {
            AppliedMods.Clear();
            foreach (ModListEntry entry in settings.Packs[SelectedPack])
            {
                // entries hold just the file name, mods always live in the mods folder
                ModItem? mod = AvailableMods.FirstOrDefault(m => m.FileName == Path.GetFileName(entry.File));
                if (mod is null)
                {
                    AddLog(new LogLine($"{Path.GetFileName(entry.File)} is missing from the mods folder, dropped it from the pack.", LogLine.WarningBrush));
                    continue;
                }

                AppliedMods.Add(new AppliedMod(mod, entry.Enabled));
            }
        }
        finally
        {
            m_isLoadingPack = false;
        }

        SavePack();
    }

    private void SavePack()
    {
        if (m_isLoadingPack || GameSettings is not { } settings || SelectedPack is null)
        {
            return;
        }

        foreach (AppliedMod applied in AppliedMods)
        {
            // toggling a checkbox has to save too, resubscribe so every entry has exactly one handler
            applied.PropertyChanged -= OnAppliedChanged;
            applied.PropertyChanged += OnAppliedChanged;
        }

        settings.Packs[SelectedPack] =
            AppliedMods.Select(a => new ModListEntry { File = a.Mod.FileName, Enabled = a.IsEnabled }).ToList();
        m_settings.Save();
    }

    private void OnAppliedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SavePack();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task AddPackAsync()
    {
        string? name = await PromptAsync("New Pack", "Name of the new pack:");
        if (name is null || GameSettings is not { } settings)
        {
            return;
        }

        if (!settings.Packs.ContainsKey(name))
        {
            settings.Packs[name] = new List<ModListEntry>();
            Packs.Add(name);
        }

        SelectedPack = name;
    }

    [RelayCommand(CanExecute = nameof(CanRemovePack))]
    private async Task RemovePackAsync()
    {
        if (SelectedPack is null || GameSettings is not { } settings ||
            !await ConfirmAsync("Remove Pack", $"Delete the pack \"{SelectedPack}\"? The mod files are kept."))
        {
            return;
        }

        string pack = SelectedPack;
        settings.Packs.Remove(pack);
        SelectedPack = Settings.DefaultPack;
        Packs.Remove(pack);
    }

    private bool CanRemovePack() => CanEdit() && SelectedPack is not null && SelectedPack != Settings.DefaultPack;

    private bool CanEdit() => !IsBusy && State == SessionState.Ready;
    private bool CanEditApplied() => !IsBusy && SelectedApplied is not null;

    partial void OnSelectedAppliedChanged(AppliedMod? value)
    {
        if (value is not null)
        {
            DescribedMod = value.Mod;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditApplied))]
    private void RemoveApplied()
    {
        if (SelectedApplied is null)
        {
            return;
        }

        int index = AppliedMods.IndexOf(SelectedApplied);
        AppliedMods.Remove(SelectedApplied);
        SelectedApplied = AppliedMods.Count == 0 ? null : AppliedMods[Math.Min(index, AppliedMods.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(CanEditApplied))]
    private void MoveAppliedUp() => MoveApplied(-1);

    [RelayCommand(CanExecute = nameof(CanEditApplied))]
    private void MoveAppliedDown() => MoveApplied(1);

    private void MoveApplied(int inOffset)
    {
        if (SelectedApplied is null)
        {
            return;
        }

        int index = AppliedMods.IndexOf(SelectedApplied);
        int target = index + inOffset;
        if (target < 0 || target >= AppliedMods.Count)
        {
            return;
        }

        AppliedMod item = SelectedApplied;
        AppliedMods.Move(index, target);
        SelectedApplied = item;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ClearApplied() => AppliedMods.Clear();

    #endregion

    #region Compile and launch

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task CompileAsync()
    {
        if (m_session is null || SelectedPack is null)
        {
            return;
        }

        if (InitFsKeyFinder.FindGameProcess(m_session.Game.ExeName) is not null)
        {
            AddLog(new LogLine("Close the game before compiling, its files are about to be replaced.", LogLine.ErrorBrush));
            return;
        }

        List<AppliedMod> enabled = AppliedMods.Where(a => a.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            await RestoreVanillaAsync();
            return;
        }

        if (!await ConfirmAsync("Compile",
                $"Frosty will generate the modded data for {enabled.Count} mod(s) and install it into the game folder.\n\n" +
                "The original files are moved to FrostyBackup inside the game folder, Restore Vanilla puts them back.\n\n" +
                "Continue?"))
        {
            return;
        }

        // the executor keeps the first change to an asset, the applied list is bottom-wins like Frosty 1.x
        List<string> modPaths = enabled.Select(a => a.Mod.FilePath).Reverse().ToList();
        List<string> titles = enabled.Select(a => a.Mod.Title).ToList();
        string pack = SelectedPack;
        GameSession session = m_session;

        Status = "Compiling…";
        LibraryTab = 1;
        bool success = false;
        await RunBusyAsync(() =>
        {
            GameInstaller.Uninstall(session.Game);
            if (!session.Apply(modPaths))
            {
                return;
            }

            GameInstaller.Install(session.Game, session.ModDataDirectory, pack, titles);
            success = WineLauncher.PrepareGame(session.Game);
        });

        UpdateInstalledStatus();
        Status = success ? "Compiled. Launch the game from here or from Steam." : "Compile failed, see the Log tab.";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RestoreVanillaAsync()
    {
        if (m_session is null)
        {
            return;
        }

        if (GameInstaller.GetInstalled(m_session.Game) is null)
        {
            AddLog(new LogLine("The game is already vanilla.", LogLine.InfoBrush));
            return;
        }

        if (InitFsKeyFinder.FindGameProcess(m_session.Game.ExeName) is not null)
        {
            AddLog(new LogLine("Close the game before restoring vanilla files.", LogLine.ErrorBrush));
            return;
        }

        GameSession session = m_session;
        Status = "Restoring vanilla files…";
        await RunBusyAsync(() => GameInstaller.Uninstall(session.Game));
        UpdateInstalledStatus();
        Status = "Vanilla restored.";
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (SelectedGame is null)
        {
            return;
        }

        InstallManifest? installed = m_session is null ? null : GameInstaller.GetInstalled(m_session.Game);
        List<string> wanted = AppliedMods.Where(a => a.IsEnabled).Select(a => a.Mod.Title).ToList();
        bool stale = installed is null ? wanted.Count > 0 : installed.Pack != SelectedPack || !installed.Mods.SequenceEqual(wanted);
        if (stale && State == SessionState.Ready &&
            await ConfirmAsync("Launch", "The applied mods changed since the last compile. Compile before launching?"))
        {
            await CompileAsync();
        }

        WineLauncher.Launch(SelectedGame);
    }

    private bool CanLaunch() => !IsBusy && SelectedGame is not null;

    private void UpdateInstalledStatus()
    {
        InstallManifest? installed = m_session is null ? null : GameInstaller.GetInstalled(m_session.Game);
        IsModded = installed is not null;
        InstalledStatus = installed is null
            ? "Vanilla"
            : $"Modded · {installed.Pack} · {installed.Mods.Count} mod{(installed.Mods.Count == 1 ? "" : "s")}";
    }

    [RelayCommand]
    private void OpenModsFolder()
    {
        if (m_session is not null && State == SessionState.Ready)
        {
            Directory.CreateDirectory(m_session.ModsDirectory);
            OpenFolder(m_session.ModsDirectory);
        }
    }

    [RelayCommand]
    private void OpenGameFolder()
    {
        if (SelectedGame is not null)
        {
            OpenFolder(SelectedGame.Directory);
        }
    }

    private static void OpenFolder(string inPath)
    {
        ProcessStartInfo info = new("xdg-open") { UseShellExecute = false };
        info.ArgumentList.Add(inPath);
        Process.Start(info);
    }

    #endregion

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
