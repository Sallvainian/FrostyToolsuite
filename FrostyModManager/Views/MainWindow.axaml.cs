using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FrostyModManager.ViewModels;

namespace FrostyModManager.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType s_modFiles = new("Frosty mods")
    {
        Patterns = new[] { "*.fbmod", "*.fbcollection", "*.zip" }
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
        if (DataContext is MainWindowViewModel vm)
        {
            // keep the newest log line in view
            vm.Log.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add && vm.Log.Count > 0)
                {
                    LogList.ScrollIntoView(vm.Log.Count - 1);
                }
            };
        }
    }

    private async void OnAddMods(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add mods",
            AllowMultiple = true,
            FileTypeFilter = new[] { s_modFiles, FilePickerFileTypes.All }
        });

        await ViewModel.ImportAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>());
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
