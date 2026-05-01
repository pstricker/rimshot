using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DrumApp.Models;
using DrumApp.Services;
using static DrumApp.Services.CueEngineState;

namespace DrumApp;

public partial class MainWindow : Window
{
    private readonly MidiService _midi = new();
    private readonly CueEngine _cueEngine = new();
    private readonly AudioService _audio = new();
    private readonly MetronomeService _metronome = new();
    private readonly ObservableCollection<string> _hits = [];
    private bool _connected;
    private bool _hiHatOpen = false;
    private bool _suppressBpmUpdate = false;

    public MainWindow()
    {
        InitializeComponent();

        HitLog.ItemsSource = _hits;

        var devices = _midi.GetInputDevices();
        DeviceCombo.ItemsSource = devices;
        if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;

        _midi.DrumHitReceived += OnDrumHit;

        TheCueView.Midi = _midi;
        TheCueView.Engine = _cueEngine;
        TheCueView.Metronome = _metronome;
        TheCueView.Audio = _audio;

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
        UpdateTransportButtons();
    }

    private void OnRestartClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _cueEngine.Restart();
        _metronome.Start();
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

    private void OnClearClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _hits.Clear();

    private void OnDrumHit(object? sender, DrumHit hit)
    {
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
        { Key.F,     3 }, // TM (hi-mid)
        { Key.Space, 4 }, // BD
        { Key.J,     5 }, // TM (floor)
        { Key.K,     6 }, // RD
    };

    private static readonly IReadOnlyList<DrumLane> _kitLanes = DrumLane.StandardKit();
    private readonly System.Collections.Generic.HashSet<Key> _heldKeys = [];

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_heldKeys.Add(e.Key)) return; // already held — suppress repeat
        if (!_keyToLane.TryGetValue(e.Key, out int lane)) return;

        var laneDef = _kitLanes[lane];
        var hit = new DrumHit(System.DateTime.Now, laneDef.Label, laneDef.NoteNumbers[0], 100);
        _midi.TriggerManualHit(hit);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        _heldKeys.Remove(e.Key);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _cueEngine.Stop();
        _metronome.Stop();
        _midi.Dispose();
        _audio.Dispose();
        base.OnClosing(e);
    }
}
