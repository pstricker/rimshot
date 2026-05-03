using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Rimshot.Core.Models;
using Rimshot.Core.Services;
using Rimshot.Services;
using Rimshot.Views;
using static Rimshot.Services.CueEngineState;

namespace Rimshot;

public partial class MainWindow : Window
{
    private readonly MidiService _midi = new();
    private readonly LoopSelectionService _loopSelection = new();
    private readonly CueEngine _cueEngine;
    private readonly AudioService _audio = new();
    private readonly MetronomeService _metronome = new();
    private readonly MusicService _music;
    private readonly MidiLibraryService _midiLibrary = new();
    private readonly ObservableCollection<string> _hits = [];
    private bool _hiHatOpen = false;
    private bool _suppressBpmUpdate = false;
    private bool _suppressBackingToggle = false;
    private bool _autoPlay = false;

    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";

    public MainWindow()
    {
        _cueEngine = new CueEngine(_loopSelection);
        InitializeComponent();

        Title = $"Rimshot {AppVersion}";

        _music = new MusicService(_audio);

        // Initialize loop service with the engine's default song length.
        // CLEAR LOOP visibility is now handled by SongTimelineView itself
        // (the ✕ button is rendered inline with the timeline strip), so
        // MainWindow no longer subscribes to LoopChanged for that purpose.
        _loopSelection.SetSongLength(_cueEngine.CurrentSong.TotalEighths);

        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        HitLog.ItemsSource = _hits;

        TheLibraryView.Configure(_midiLibrary, SongLibrary.Groups);
        TheLibraryView.SongSelected += OnLibrarySongSelected;
        TheLibraryView.CloseRequested += CloseLibrary;
        TheLibraryView.StatusMessage += msg => SongStatusLabel.Text = msg;
        _ = _midiLibrary.LoadAsync();

        // Default song matches CueEngine's startup song (Rock Beat).
        LoadSong(_cueEngine.CurrentSong);

        _cueEngine.SongEnded += (_, _) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateTransportButtons();
                SongStatusLabel.Text = "Finished";
            });

        // Silence any backing-track notes still ringing across a loop wrap so
        // the next iteration starts cleanly even if a NoteOff was missed.
        _cueEngine.LoopWrapped += (_, _) => _music.Reset();

        _midi.DrumHitReceived += OnDrumHit;
        RefreshConnectionBadge();

        TheCueView.Midi = _midi;
        TheCueView.Engine = _cueEngine;
        TheCueView.Metronome = _metronome;
        TheCueView.Audio = _audio;
        TheCueView.Music = _music;
        TheCueView.Loop = _loopSelection;

        TheTimelineView.Engine    = _cueEngine;
        TheTimelineView.Loop      = _loopSelection;
        TheTimelineView.SetSong(_cueEngine.CurrentSong);

        TheTempoView.BpmChanged += bpm =>
        {
            _cueEngine.Bpm = bpm;
            _metronome.Bpm = bpm;
            _suppressBpmUpdate = true;
            BpmControl.Value = bpm;
            _suppressBpmUpdate = false;
        };
        TheTempoView.SubdivisionChanged += sub => _metronome.Subdivision = sub;

    }

    private void OnAboutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        new AboutWindow().ShowDialog(this);

    private async void OnConnectDrumsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await new ConnectDrumsDialog(_midi).ShowDialog(this);
        RefreshConnectionBadge();
    }

    private void RefreshConnectionBadge()
    {
        if (_midi.IsConnected)
        {
            ConnectDrumsButton.IsVisible = false;
            ConnectedDrumsBadge.IsVisible = true;
            ConnectedDeviceLabel.Text = _midi.ConnectedDeviceName ?? "";
        }
        else
        {
            ConnectDrumsButton.IsVisible = true;
            ConnectedDrumsBadge.IsVisible = false;
        }
    }

    private void OnPlayClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool wasPaused = _cueEngine.State == CueEngineState.Paused;
        _cueEngine.Play();
        if (wasPaused) _metronome.Resume(); else _metronome.Start();
        UpdateTransportButtons();
    }

    private void OnPauseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _cueEngine.Pause();
        _metronome.Pause();
        _music.Reset();
        UpdateTransportButtons();
    }

    private void OnRestartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TheCueView.ClearCues();
        _cueEngine.Restart();
        if (_loopSelection.IsActive) _cueEngine.Seek(_loopSelection.StartEighths);
        _metronome.Start();
        _music.Reset();
        UpdateTransportButtons();
    }

    private void OnStopClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TheCueView.ClearCues();
        _cueEngine.Stop();
        _metronome.Stop();
        _music.Reset();
        UpdateTransportButtons();
    }

    private void UpdateTransportButtons()
    {
        PlayButton.IsEnabled = _cueEngine.State != Running;
        PauseButton.IsEnabled = _cueEngine.State == Running;
        StopButton.IsEnabled = _cueEngine.State != Stopped;
    }

    private void OnBpmChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressBpmUpdate) return;
        int bpm = (int)(e.NewValue ?? 90);
        _cueEngine.Bpm = bpm;
        _metronome.Bpm = bpm;
        TheTempoView.SetBpm(bpm);
    }

    private void OnLibraryButtonClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => OpenLibrary();

    private void OpenLibrary()
    {
        LibraryOverlay.IsVisible = true;
        TheLibraryView.Open();
    }

    private void CloseLibrary() => LibraryOverlay.IsVisible = false;

    private void OnLibrarySongSelected(Song song)
    {
        LoadSong(song);
        CloseLibrary();
    }

    private void LoadSong(Song song)
    {
        _loopSelection.ClearLoop();
        _loopSelection.SetSongLength(song.TotalEighths);
        _cueEngine.LoadSong(song);
        TheCueView.SetActiveLanes(GetActiveLanes(song));
        // Defense-in-depth: a failure inside the timeline rebuild must not
        // suppress the BACKING TRACK checkbox or the status label below.
        try { TheTimelineView.SetSong(song); }
        catch (Exception ex) { Console.Error.WriteLine($"SetSong failed: {ex}"); }
        LibraryButtonLabel.Text = song.Name;
        SongStatusLabel.Text = $"{song.Notes.Length} notes, {song.TotalEighths / 8.0:F0} bars";
        UpdateBackingTrackVisibility(song);
    }

    private void OnClearClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _hits.Clear();

    private void OnAutoPlayChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _autoPlay = AutoPlayCheck.IsChecked == true;
        TheCueView.AutoPlay = _autoPlay;
    }

    private void OnBackingTrackChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_suppressBackingToggle) return;

        if (BackingTrackCheck.IsChecked != true)
        {
            _music.SetEnabled(false);
            return;
        }

        if (_music.IsAvailable)
        {
            _music.SetEnabled(true);
            return;
        }

        _ = PromptForSoundFontAsync();
    }

    private async Task PromptForSoundFontAsync()
    {
        bool wantsBrowse = await new SoundFontPromptWindow().ShowDialog<bool>(this);
        if (!wantsBrowse)
        {
            UncheckBackingTrack();
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a General MIDI SoundFont (.sf2)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SoundFont 2") { Patterns = ["*.sf2"] },
            ],
        });

        if (files.Count == 0)
        {
            UncheckBackingTrack();
            return;
        }

        SongStatusLabel.Text = "Loading soundfont…";
        bool ok = await InstallSoundFontAsync(files[0].Path.LocalPath);
        if (ok)
        {
            _music.SetEnabled(true);
            SongStatusLabel.Text = "Soundfont loaded.";
        }
        else
        {
            UncheckBackingTrack();
        }
    }

    private async Task<bool> InstallSoundFontAsync(string sourcePath)
    {
        try
        {
            string destDir  = Path.Combine(AppContext.BaseDirectory, "Sounds", "soundfonts");
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, Path.GetFileName(sourcePath));

            // Skip the copy if the user picked the file from inside the
            // destination folder — File.Copy(src, src) would throw.
            if (!string.Equals(
                    Path.GetFullPath(sourcePath),
                    Path.GetFullPath(destPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, destPath, overwrite: true);
            }

            return await _music.LoadSoundFontAsync(destPath);
        }
        catch (Exception ex)
        {
            SongStatusLabel.Text = $"Soundfont install failed: {ex.Message}";
            return false;
        }
    }

    private void UncheckBackingTrack()
    {
        _suppressBackingToggle = true;
        BackingTrackCheck.IsChecked = false;
        _suppressBackingToggle = false;
        _music.SetEnabled(false);
    }

    private void UpdateBackingTrackVisibility(Song song)
    {
        BackingTrackCheck.IsVisible = song.HasBackingTrack;
        UncheckBackingTrack();
        _music.Reset();
    }

    private void OnDrumHit(object? sender, DrumHit hit)
    {
        if (_autoPlay) return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = 0; i < _kitLanes.Count; i++)
            {
                if (Array.IndexOf(_kitLanes[i].NoteNumbers, hit.NoteNumber) >= 0)
                {
                    if (i == 0)
                    {
                        _hiHatOpen = hit.NoteNumber == 46;
                        TheCueView.SetHiHatOpen(_hiHatOpen);
                    }
                    _audio.Play(i, hit.NoteNumber, hit.Velocity / 127f);
                    break;
                }
            }

            _hits.Insert(0, hit.Display);
            if (_hits.Count > 200)
                _hits.RemoveAt(_hits.Count - 1);
        });
    }

    private static readonly IReadOnlyDictionary<Key, int> _keyToLane = new Dictionary<Key, int>
    {
        { Key.A,     0 }, // HH
        { Key.S,     1 }, // CR
        { Key.D,     2 }, // SN
        { Key.F,     3 }, // TM1
        { Key.G,     4 }, // TM2
        { Key.Space, 5 }, // BD
        { Key.J,     6 }, // FTM
        { Key.K,     7 }, // RD
    };

    private static readonly IReadOnlyList<DrumLane> _kitLanes = DrumLane.StandardKit();
    private readonly System.Collections.Generic.HashSet<Key> _heldKeys = [];

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (!_heldKeys.Add(e.Key)) return; // already held — suppress repeat
        if (!_keyToLane.TryGetValue(e.Key, out int lane)) return;

        var laneDef = _kitLanes[lane];
        var hit = new DrumHit(System.DateTime.Now, laneDef.Label, laneDef.NoteNumbers[0], 100);
        _midi.TriggerManualHit(hit);
        e.Handled = true; // prevent focused controls (e.g. ComboBox) from consuming drum keys
    }

    private void OnKeyUpTunnel(object? sender, KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
    }

    private static IReadOnlyList<DrumLane> GetActiveLanes(Song song)
    {
        if (song.Notes.Length == 0) return DrumLane.StandardKit();
        var usedIndices = new HashSet<int>(song.Notes.Select(n => n.Lane));
        return DrumLane.StandardKit().Where(l => usedIndices.Contains(l.Index)).ToList();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _audio.PlayStartup();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _cueEngine.Stop();
        _metronome.Stop();
        _midi.Dispose();
        _music.Dispose();   // must run before _audio: streaming source lives on AudioService's context
        _audio.Dispose();
        base.OnClosing(e);
    }
}
