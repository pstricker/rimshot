using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
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
    private readonly ObservableCollection<string> _hits = [];
    private readonly ObservableCollection<Song> _songItems = new(SongLibrary.AllItems);
    private bool _connected;
    private bool _hiHatOpen = false;
    private bool _suppressBpmUpdate = false;
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
        _loopSelection.SetSongLength(_cueEngine.CurrentSong.TotalEighths);
        _loopSelection.LoopChanged += (_, _) =>
            Dispatcher.UIThread.InvokeAsync(UpdateClearLoopButton);

        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUpTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        HitLog.ItemsSource = _hits;

        SongCombo.ItemsSource = _songItems;
        SongCombo.DisplayMemberBinding = new Binding("Name");
        SongCombo.SelectedIndex = 0;

        _cueEngine.SongEnded += (_, _) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateTransportButtons();
                SongStatusLabel.Text = "Finished";
            });

        var devices = _midi.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;

        _midi.DrumHitReceived += OnDrumHit;

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

    private void OnConnectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_connected)
        {
            _midi.Disconnect();
            _connected = false;
            ConnectButton.Content = "Connect";
            StatusLabel.Text = "Not connected";
            StatusLabel.Foreground = Avalonia.Media.Brushes.Gray;
            return;
        }

        if (DeviceCombo.SelectedItem is not string deviceName)
        {
            StatusLabel.Text = "Select a device first.";
            return;
        }

        _midi.Connect(deviceName);
        _connected = true;
        ConnectButton.Content = "Disconnect";
        StatusLabel.Text = $"Connected to {deviceName}";
        StatusLabel.Foreground = Avalonia.Media.Brushes.Green;
    }

    private void OnRefreshClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var devices = _midi.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;
        StatusLabel.Text = $"Found {devices.Count} device(s)";
        StatusLabel.Foreground = Avalonia.Media.Brushes.Gray;
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

    private void UpdateTransportButtons()
    {
        PlayButton.IsEnabled = _cueEngine.State != Running;
        PauseButton.IsEnabled = _cueEngine.State == Running;
    }

    private void OnBpmChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_suppressBpmUpdate) return;
        int bpm = (int)(e.NewValue ?? 90);
        _cueEngine.Bpm = bpm;
        _metronome.Bpm = bpm;
        TheTempoView.SetBpm(bpm);
    }

    private void OnSongSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SongCombo.SelectedItem is not Song selected) return;

        if (selected == SongLibrary.LoadFromFile)
        {
            _ = LoadFromFileAsync();
            return;
        }

        _loopSelection.ClearLoop();
        _loopSelection.SetSongLength(selected.TotalEighths);
        _cueEngine.LoadSong(selected);
        TheCueView.SetActiveLanes(GetActiveLanes(selected));
        TheTimelineView.SetSong(selected);
        SongStatusLabel.Text = $"{selected.Notes.Length} notes";
        UpdateBackingTrackVisibility(selected);
    }

    private async Task LoadFromFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load MIDI File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("MIDI Files") { Patterns = ["*.mid", "*.midi"] },
            ],
        });

        if (files.Count == 0)
        {
            SongCombo.SelectedIndex = 0;
            return;
        }

        try
        {
            SongStatusLabel.Text = "Loading…";
            var song = await Task.Run(() => SongLoader.Load(files[0].Path.LocalPath));

            // Insert before the sentinel ("Load from file…") at the end
            _songItems.Insert(_songItems.Count - 1, song);
            SongCombo.SelectedItem = song;

            _loopSelection.ClearLoop();
            _loopSelection.SetSongLength(song.TotalEighths);
            _cueEngine.LoadSong(song);
            TheCueView.SetActiveLanes(GetActiveLanes(song));
            TheTimelineView.SetSong(song);
            SongStatusLabel.Text = $"{song.Notes.Length} notes, {song.TotalEighths / 8.0:F0} bars";
            UpdateBackingTrackVisibility(song);
        }
        catch (Exception ex)
        {
            SongStatusLabel.Text = $"Error: {ex.Message}";
            SongCombo.SelectedIndex = 0;
        }
    }

    private void OnClearClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _hits.Clear();

    private void OnClearLoopClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _loopSelection.ClearLoop();

    private void UpdateClearLoopButton()
    {
        ClearLoopButton.IsEnabled = _loopSelection.IsActive;
        ClearLoopButton.Foreground = _loopSelection.IsActive
            ? Avalonia.Media.Brushes.HotPink
            : Avalonia.Media.Brushes.Gray;
    }

    private void OnAutoPlayChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _autoPlay = AutoPlayCheck.IsChecked == true;
        TheCueView.AutoPlay = _autoPlay;
    }

    private void OnBackingTrackChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _music.SetEnabled(BackingTrackCheck.IsChecked == true);
    }

    private void UpdateBackingTrackVisibility(Song song)
    {
        bool visible = song.HasBackingTrack && _music.IsAvailable;
        BackingTrackCheck.IsVisible = visible;
        BackingTrackCheck.IsChecked = false;
        _music.SetEnabled(false);
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
