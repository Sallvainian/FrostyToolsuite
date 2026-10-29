using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FrostyModManager.Models;
using FrostyModManager.ViewModels;

namespace FrostyModManager.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType s_modFiles = new("Frosty mods")
    {
        Patterns = new[] { "*.fbmod", "*.fbcollection", "*.zip", "*.rar", "*.7z" }
    };

    private static readonly FilePickerFileType s_exeFiles = new("Game executable")
    {
        Patterns = new[] { "*.exe" }
    };

    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.ConfirmAsync = (title, message) => Dialogs.ConfirmAsync(this, title, message);
        vm.PromptAsync = (title, label) => Dialogs.PromptAsync(this, title, label);

        // keep the newest log line in view
        vm.Log.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Add && vm.Log.Count > 0)
            {
                LogList.ScrollIntoView(vm.Log.Count - 1);
            }
        };
    }

    private List<ModItem> SelectedAvailable() => LibraryList.SelectedItems?.OfType<ModItem>().ToList() ?? new List<ModItem>();

    private async void OnAddMods(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add Mod(s)",
            AllowMultiple = true,
            FileTypeFilter = new[] { s_modFiles, FilePickerFileTypes.All }
        });

        await ViewModel.ImportAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    private async void OnRemoveMods(object? sender, RoutedEventArgs e) => await ViewModel.RemoveModsAsync(SelectedAvailable());

    private void OnLibraryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (LibraryList.SelectedItem is ModItem mod)
        {
            ViewModel.ApplyOneCommand.Execute(mod);
        }
    }

    private void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // category rows are labels, never selections
        foreach (CategoryHeader header in e.AddedItems.OfType<CategoryHeader>())
        {
            LibraryList.SelectedItems?.Remove(header);
        }

        if (e.AddedItems.OfType<ModItem>().LastOrDefault() is { } mod)
        {
            ViewModel.DescribedMod = mod;
        }
    }

    private async void OnBrowseGame(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select the game's executable",
            FileTypeFilter = new[] { s_exeFiles }
        });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            ViewModel.AddGame(path);
        }
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        await Dialogs.ConfirmAsync(this, "About",
            "Frosty Mod Manager 2.0 for Linux\n\nA native mod manager built on the FrostyToolsuite v2 rewrite.\n" +
            "github.com/Sallvainian/FrostyToolsuite (branch linux-native)");
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) && ViewModel.IsReady ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        IEnumerable<string> paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>() ?? Enumerable.Empty<string>();
        await ViewModel.ImportAsync(paths);
    }
}
