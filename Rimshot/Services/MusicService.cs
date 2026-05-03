using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MeltySynth;
using Silk.NET.OpenAL;

namespace Rimshot.Services;

// Wraps a MeltySynth soundfont synthesizer and streams its output through the
// shared OpenAL device owned by AudioService. Disabled (IsAvailable=false) if
// no .sf2 is found in Sounds/soundfonts/.
public unsafe class MusicService : IDisposable
{
    private const int SampleRate    = 44100;
    private const int BufferCount   = 4;
    private const int FramesPerBuf  = 882;          // ~20ms @ 44.1kHz
    private const float MasterGain  = 0.7f;          // headroom before clamp
    private const int LateGraceMs   = 100;           // skip events older than this on drain
    private const int RenderSleepMs = 5;

    private readonly AudioService _audio;

    private Synthesizer? _synth;
    private uint _source;
    private uint[] _buffers = Array.Empty<uint>();
    private Thread? _renderThread;
    private volatile bool _running;
    private volatile bool _enabled;
    private volatile bool _isAvailable;

    private readonly ConcurrentQueue<TimedMidiMessage> _inbox = new();
    private readonly object _resetLock = new();
    private volatile bool _resetRequested;

    public bool IsAvailable => _isAvailable;

    public MusicService(AudioService audio)
    {
        _audio = audio;
        var path = FindSoundFont();
        if (path is null) return;

        // Load synth + start streaming on a background thread so a 25 MB
        // soundfont parse doesn't block the UI thread at startup.
        _ = LoadSoundFontAsync(path);
    }

    private static string? FindSoundFont()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Sounds", "soundfonts");
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, "*.sf2");
        return files.Length > 0 ? files[0] : null;
    }

    /// <summary>
    /// Loads (or replaces) the active soundfont. Safe to call from any thread;
    /// the parse + OpenAL setup runs synchronously on the calling thread, so
    /// callers should invoke via Task.Run when invoked from the UI thread.
    /// Returns true on success.
    /// </summary>
    public Task<bool> LoadSoundFontAsync(string path) =>
        Task.Run(() =>
        {
            try
            {
                if (_isAvailable) TeardownSynth();

                _synth = new Synthesizer(path, SampleRate);
                if (!_audio.IsInitialized || _audio.Al is null) return false;

                var al = _audio.Al;
                _source  = _audio.CreateStreamingSource();
                _buffers = new uint[BufferCount];

                short[] silence = new short[FramesPerBuf * 2];
                for (int i = 0; i < BufferCount; i++)
                {
                    _buffers[i] = al.GenBuffer();
                    fixed (short* ptr = silence)
                        al.BufferData(_buffers[i], BufferFormat.Stereo16,
                            ptr, silence.Length * sizeof(short), SampleRate);
                    fixed (uint* bptr = &_buffers[i])
                        al.SourceQueueBuffers(_source, 1, bptr);
                }
                al.SetSourceProperty(_source, SourceFloat.Gain, 1.0f);
                al.SourcePlay(_source);

                _running = true;
                _renderThread = new Thread(RenderLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal,
                    Name = "MusicService-Render",
                };
                _renderThread.Start();

                _isAvailable = true;
                return true;
            }
            catch
            {
                _isAvailable = false;
                _synth = null;
                return false;
            }
        });

    private void TeardownSynth()
    {
        _running = false;
        _renderThread?.Join(TimeSpan.FromMilliseconds(500));
        _renderThread = null;

        var al = _audio.Al;
        if (al is not null && _source != 0)
        {
            try
            {
                al.SourceStop(_source);
                al.DeleteSource(_source);
                foreach (uint b in _buffers)
                    al.DeleteBuffer(b);
            }
            catch
            {
                // OpenAL context may already be torn down — best effort.
            }
        }
        _source = 0;
        _buffers = Array.Empty<uint>();
        _synth = null;
        _isAvailable = false;
        while (_inbox.TryDequeue(out _)) { }
    }

    public void Schedule(DateTime at, int channel, byte command, byte data1, byte data2)
    {
        if (!_isAvailable || !_enabled) return;
        _inbox.Enqueue(new TimedMidiMessage(at, channel, command, data1, data2));
    }

    public void SetEnabled(bool on)
    {
        if (_enabled == on) return;
        _enabled = on;
        if (!on) Reset();
    }

    public void Reset()
    {
        // Render thread will drain the inbox and silence all channels on next iteration.
        _resetRequested = true;
    }

    private void RenderLoop()
    {
        var al = _audio.Al!;
        var pending = new List<TimedMidiMessage>(64);
        float[] left  = new float[FramesPerBuf];
        float[] right = new float[FramesPerBuf];
        short[] interleaved = new short[FramesPerBuf * 2];

        while (_running)
        {
            HandleResetIfNeeded(pending);
            DrainInbox(pending);
            ApplyDueMessages(pending);

            int processed = 0;
            al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out processed);
            if (processed == 0)
            {
                Thread.Sleep(RenderSleepMs);
                continue;
            }

            while (processed-- > 0 && _running)
            {
                uint reclaimed = 0;
                al.SourceUnqueueBuffers(_source, 1, &reclaimed);

                _synth!.Render(left, right);
                InterleaveAndConvert(left, right, interleaved);

                fixed (short* ptr = interleaved)
                    al.BufferData(reclaimed, BufferFormat.Stereo16,
                        ptr, interleaved.Length * sizeof(short), SampleRate);
                al.SourceQueueBuffers(_source, 1, &reclaimed);
            }

            // Recover from underrun.
            al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing)
                al.SourcePlay(_source);
        }
    }

    private void HandleResetIfNeeded(List<TimedMidiMessage> pending)
    {
        if (!_resetRequested) return;
        _resetRequested = false;

        pending.Clear();
        while (_inbox.TryDequeue(out _)) { }

        // All Sound Off (CC#120) on every channel kills hung notes immediately.
        for (int ch = 0; ch < 16; ch++)
            _synth!.ProcessMidiMessage(ch, 0xB0, 120, 0);
    }

    private void DrainInbox(List<TimedMidiMessage> pending)
    {
        while (_inbox.TryDequeue(out var msg))
            pending.Add(msg);
    }

    private void ApplyDueMessages(List<TimedMidiMessage> pending)
    {
        if (pending.Count == 0) return;

        var now = DateTime.Now;
        var graceCutoff = now - TimeSpan.FromMilliseconds(LateGraceMs);
        int writeIdx = 0;

        for (int i = 0; i < pending.Count; i++)
        {
            var msg = pending[i];
            if (msg.ScheduledAt <= now)
            {
                // Skip stale events (e.g., enqueued during long SF2 load).
                if (msg.ScheduledAt >= graceCutoff)
                    _synth!.ProcessMidiMessage(msg.Channel, msg.Command, msg.Data1, msg.Data2);
            }
            else
            {
                pending[writeIdx++] = msg;
            }
        }
        pending.RemoveRange(writeIdx, pending.Count - writeIdx);
    }

    private static void InterleaveAndConvert(float[] left, float[] right, short[] dest)
    {
        for (int i = 0; i < left.Length; i++)
        {
            float l = Math.Clamp(left[i]  * MasterGain, -1f, 1f);
            float r = Math.Clamp(right[i] * MasterGain, -1f, 1f);
            dest[2 * i]     = (short)(l * short.MaxValue);
            dest[2 * i + 1] = (short)(r * short.MaxValue);
        }
    }

    public void Dispose() => TeardownSynth();

    private readonly record struct TimedMidiMessage(
        DateTime ScheduledAt,
        int Channel,
        byte Command,
        byte Data1,
        byte Data2);
}
