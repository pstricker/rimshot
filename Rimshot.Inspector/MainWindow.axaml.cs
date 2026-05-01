using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Rimshot.Core.Services;

namespace Rimshot.Inspector;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(DropZone, true);
        DropZone.AddHandler(DragDrop.DropEvent, OnFileDrop);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var dt = e.DataTransfer;
        e.DragEffects = dt is not null && dt.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        var dt = e.DataTransfer;
        if (dt is null) return;

        foreach (var item in dt.Items)
        {
            if (item.TryGetRaw(DataFormat.File) is not IStorageItem storageItem) continue;

            var path = storageItem.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;

            if (!path.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            {
                ShowError("Only .mid and .midi files are supported.");
                return;
            }

            _ = LoadFileAsync(path);
            return;
        }
    }

    private void OnBrowseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _ = BrowseAsync();

    private async Task BrowseAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MIDI File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MIDI Files") { Patterns = ["*.mid", "*.midi"] },
            ],
        });

        if (files.Count == 0) return;
        await LoadFileAsync(files[0].Path.LocalPath);
    }

    private async Task LoadFileAsync(string path)
    {
        ErrorLabel.IsVisible = false;
        try
        {
            var result = await Task.Run(() => MidiAnalysis.Analyze(path));
            PopulateResults(result);

            DropZone.IsVisible      = false;
            ResultsScroll.IsVisible = true;
        }
        catch (Exception ex)
        {
            ShowError($"Could not read file: {ex.Message}");
        }
    }

    private void PopulateResults(MidiAnalysisResult r)
    {
        FileNameLabel.Text   = r.FileName;
        FormatLabel.Text     = r.Format;
        TrackCountLabel.Text = $"{r.TrackCount} track{(r.TrackCount != 1 ? "s" : "")}";
        DurationLabel.Text   = r.Duration.TotalSeconds >= 60
            ? $"{(int)r.Duration.TotalMinutes}:{r.Duration.Seconds:D2}"
            : $"{r.Duration.TotalSeconds:F1}s";
        NoteCountLabel.Text  = $"{r.TotalNoteCount} notes";

        PrimaryBpmLabel.Text = $"{r.PrimaryBpm:F1} BPM";
        if (r.TempoChanges.Count > 1)
        {
            TempoChangesList.ItemsSource = r.TempoChanges
                .Select(tc => new
                {
                    DisplayText = $"  {tc.Offset:mm\\:ss\\.fff}  →  {tc.Bpm:F1} BPM",
                })
                .ToList();
            TempoChangesList.IsVisible = true;
        }
        else
        {
            TempoChangesList.IsVisible = false;
        }

        TimeSigList.ItemsSource = r.TimeSignatures.Count > 0
            ? r.TimeSignatures
                .Select(ts => new
                {
                    DisplayText = $"{ts.Numerator}/{ts.Denominator}"
                                  + (ts.Offset > TimeSpan.Zero ? $"  @ {ts.Offset:mm\\:ss}" : ""),
                })
                .ToList<object>()
            : [new { DisplayText = "4/4 (default)" }];

        ChannelList.ItemsSource = r.Channels.Select(ch => new
        {
            ChannelLabel   = $"CH {ch.ChannelNumber + 1}",
            RoleLabel      = ch.IsDrumChannel ? "Drums (GM)" : "Melodic",
            NoteCountLabel = $"{ch.NoteCount} notes",
            ChannelColor   = ch.IsDrumChannel ? "#FFD700" : "#00D9FF",
        }).ToList();

        var drumCh = r.Channels.FirstOrDefault(c => c.IsDrumChannel);
        if (drumCh?.DrumInstruments is { Count: > 0 } instruments)
        {
            DrumInstrumentList.ItemsSource = instruments.Select(d => new
            {
                d.InstrumentName,
                HitCountLabel = $"{d.HitCount}×",
            }).ToList();
            DrumSection.IsVisible = true;

            KitCoverageList.ItemsSource = r.KitCoverage.Select(c =>
            {
                bool used = c.HitCount > 0;
                return new
                {
                    LaneLabel      = c.Label,
                    LaneColor      = used ? LaneColorFor(c.LaneIndex) : "#444444",
                    StatusLabel    = used ? "active" : "not used",
                    HitCountLabel  = used ? $"{c.HitCount}×" : "—",
                    HitCountColor  = used ? "#FFFFFF" : "#444444",
                };
            }).ToList();

            if (r.UnmappedDrums.Count > 0)
            {
                UnmappedList.ItemsSource = r.UnmappedDrums.Select(u => new
                {
                    Label         = $"{u.Name} (note {u.NoteNumber})",
                    HitCountLabel = $"{u.HitCount}×",
                }).ToList();
                UnmappedSection.IsVisible = true;
            }
            else
            {
                UnmappedSection.IsVisible = false;
            }

            KitCoverageSection.IsVisible = true;
        }
        else
        {
            DrumSection.IsVisible        = false;
            KitCoverageSection.IsVisible = false;
        }
    }

    private static string LaneColorFor(int laneIndex) => laneIndex switch
    {
        0 or 1 or 7 => "#FFD700", // cymbals — yellow
        2 or 5      => "#FF1493", // SN, BD — pink
        _           => "#00D9FF", // toms   — cyan
    };

    private void ShowError(string message)
    {
        ErrorLabel.Text      = message;
        ErrorLabel.IsVisible = true;
    }

    private void OnLoadAnotherClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DropZone.IsVisible           = true;
        ResultsScroll.IsVisible      = false;
        ErrorLabel.IsVisible         = false;
        DrumSection.IsVisible        = false;
        KitCoverageSection.IsVisible = false;
        UnmappedSection.IsVisible    = false;
        TempoChangesList.IsVisible   = false;
    }
}
