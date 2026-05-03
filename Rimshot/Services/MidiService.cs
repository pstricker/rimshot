using System;
using System.Collections.Generic;
using System.Linq;
using Rimshot.Core;
using Rimshot.Core.Models;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Rimshot.Services;

public class MidiService : IDisposable
{
    private InputDevice? _device;

    public event EventHandler<DrumHit>? DrumHitReceived;

    public string? ConnectedDeviceName { get; private set; }
    public bool IsConnected => _device is not null;

    public IReadOnlyList<string> GetInputDevices() =>
        InputDevice.GetAll().Select(d => d.Name).ToList();

    public void Connect(string deviceName)
    {
        Disconnect();

        _device = InputDevice.GetByName(deviceName);
        _device.EventReceived += OnEventReceived;
        _device.StartEventsListening();
        ConnectedDeviceName = deviceName;
    }

    public void Disconnect()
    {
        if (_device is null) return;

        _device.StopEventsListening();
        _device.EventReceived -= OnEventReceived;
        _device.Dispose();
        _device = null;
        ConnectedDeviceName = null;
    }

    private void OnEventReceived(object? sender, MidiEventReceivedEventArgs e)
    {
        if (e.Event is NoteOnEvent noteOn && noteOn.Velocity > 0)
        {
            var hit = new DrumHit(
                DateTime.Now,
                DrumMap.GetName(noteOn.NoteNumber),
                noteOn.NoteNumber,
                noteOn.Velocity);

            DrumHitReceived?.Invoke(this, hit);
        }
    }

    public void TriggerManualHit(DrumHit hit) => DrumHitReceived?.Invoke(this, hit);

    public void Dispose() => Disconnect();
}
