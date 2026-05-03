using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Rimshot.Services;

namespace Rimshot.Views;

public partial class ConnectDrumsDialog : Window
{
    private readonly MidiService _midi;

    public ConnectDrumsDialog() : this(new MidiService()) { }

    public ConnectDrumsDialog(MidiService midi)
    {
        _midi = midi;
        InitializeComponent();
        RefreshDeviceList();
        UpdateConnectionUi();
    }

    private void RefreshDeviceList()
    {
        var devices = _midi.GetInputDevices();
        DeviceCombo.ItemsSource = devices;

        var current = _midi.ConnectedDeviceName;
        if (current is not null && devices.Contains(current))
            DeviceCombo.SelectedItem = current;
        else if (devices.Count > 0)
            DeviceCombo.SelectedIndex = 0;
    }

    private void UpdateConnectionUi()
    {
        if (_midi.IsConnected)
        {
            ConnectButton.Content = "DISCONNECT";
            ConnectButton.BorderBrush = new SolidColorBrush(Color.Parse("#FF1493"));
            ConnectButton.Foreground  = new SolidColorBrush(Color.Parse("#FF1493"));
            StatusLabel.Text = $"Connected to {_midi.ConnectedDeviceName}";
            StatusLabel.Foreground = new SolidColorBrush(Color.Parse("#4CAF50"));
        }
        else
        {
            ConnectButton.Content = "CONNECT";
            ConnectButton.BorderBrush = new SolidColorBrush(Color.Parse("#FFD700"));
            ConnectButton.Foreground  = new SolidColorBrush(Color.Parse("#FFD700"));
            StatusLabel.Text = "Not connected";
            StatusLabel.Foreground = new SolidColorBrush(Color.Parse("#888888"));
        }
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        RefreshDeviceList();
        var count = (DeviceCombo.ItemsSource as System.Collections.Generic.IReadOnlyList<string>)?.Count ?? 0;
        StatusLabel.Text = $"Found {count} device(s)";
        StatusLabel.Foreground = new SolidColorBrush(Color.Parse("#888888"));
    }

    private void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        if (_midi.IsConnected)
        {
            _midi.Disconnect();
            UpdateConnectionUi();
            return;
        }

        if (DeviceCombo.SelectedItem is not string deviceName)
        {
            StatusLabel.Text = "Pick a device first.";
            StatusLabel.Foreground = new SolidColorBrush(Color.Parse("#FF1493"));
            return;
        }

        _midi.Connect(deviceName);
        UpdateConnectionUi();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
