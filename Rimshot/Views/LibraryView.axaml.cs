using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Rimshot.Core.Models;
using Rimshot.Core.Services;
using Rimshot.Services;

namespace Rimshot.Views;

public partial class LibraryView : UserControl
{
    private MidiLibraryService? _library;

    public event Action<Song>? SongSelected;
    public event Action<string>? StatusMessage;
    public event Action? CloseRequested;

    public LibraryView()
    {
        InitializeComponent();
    }

    public void Configure(MidiLibraryService library, IReadOnlyList<BuiltInGroup> groups)
    {
        _library = library;

        LibraryList.ItemsSource = _library.Entries;
        BuiltInList.ItemsSource = groups;

        _library.Entries.CollectionChanged += OnLibraryEntriesChanged;
        UpdateMyLibraryEmptyState();
    }

    public void Open()
    {
        SectionList.SelectedIndex = 0; // My Library
        Focus();
    }

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not ListBoxItem item) return;
        var tag = item.Tag as string;
        MyLibraryPane.IsVisible = tag == "library";
        BuiltInPane.IsVisible = tag == "builtin";
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private async void OnAddMidiClicked(object? sender, RoutedEventArgs e)
    {
        if (_library is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add MIDI to Library",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MIDI Files") { Patterns = ["*.mid", "*.midi"] },
            ],
        });

        if (files.Count == 0) return;

        var sourcePath = files[0].Path.LocalPath;

        try
        {
            var duplicate = _library.FindByOriginalPath(sourcePath);
            if (duplicate is not null)
            {
                if (topLevel is not Window owner) return;
                bool overwrite = await new DuplicateMidiDialog(duplicate.DisplayName)
                    .ShowDialog<bool>(owner);
                if (!overwrite)
                {
                    StatusMessage?.Invoke("Import cancelled — duplicate.");
                    return;
                }
                await _library.RemoveAsync(duplicate.Id);
            }

            StatusMessage?.Invoke("Importing…");
            await _library.ImportAsync(sourcePath);
            StatusMessage?.Invoke("Imported.");
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Import failed: {ex.Message}");
        }
    }

    private async void OnLoadEntryClicked(object? sender, RoutedEventArgs e)
    {
        if (_library is null) return;
        if (sender is not Control { DataContext: MidiLibraryEntry entry }) return;

        try
        {
            var song = await _library.LoadSongAsync(entry);
            SongSelected?.Invoke(song);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Load failed: {ex.Message}");
        }
    }

    private async void OnRemoveEntryClicked(object? sender, RoutedEventArgs e)
    {
        if (_library is null) return;
        if (sender is not Control { DataContext: MidiLibraryEntry entry }) return;

        try
        {
            await _library.RemoveAsync(entry.Id);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"Remove failed: {ex.Message}");
        }
    }

    private void OnLoadBuiltInClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: Song song }) return;
        SongSelected?.Invoke(song);
    }

    private void OnLibraryEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateMyLibraryEmptyState();

    private void UpdateMyLibraryEmptyState()
    {
        if (_library is null) return;
        bool empty = _library.Entries.Count == 0;
        MyLibraryEmpty.IsVisible = empty;
        LibraryList.IsVisible = !empty;
    }
}
