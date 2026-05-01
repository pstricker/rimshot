using System;
using System.Collections.Generic;
using System.Linq;
using Rimshot.Models;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Rimshot.Services;

public class MidiService : IDisposable
{
    private InputDevice? _device;

    public event EventHandler<DrumHit>? DrumHitReceived;

    public IReadOnlyList<string> GetInputDevices() =>
        InputDevice.GetAll().Select(d => d.Name).ToList();

    public void Connect(string deviceName)
    {
        Disconnect();

        _device = InputDevice.GetByName(deviceName);
        _device.EventReceived += OnEventReceived;
        _device.StartEventsListening();
    }

    public void Disconnect()
    {
        if (_device is null) return;

        _device.StopEventsListening();
        _device.EventReceived -= OnEventReceived;
        _device.Dispose();
        _device = null;
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
