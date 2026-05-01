using System;
using System.IO;
using Silk.NET.OpenAL;

namespace Rimshot.Services;

public unsafe class AudioService : IDisposable
{
    private const int LaneCount = 8;
    private const int PoolSize = 4;
    private const int SampleRate = 44100;

    private static readonly string[] _fileNames = ["hh", "cr", "sn", "tm_hi", "tm_low", "bd", "tm_fl", "rd"];
    // Per-lane gain trim — compensates for sample-level loudness differences.
    // BD is boosted because it sits in a low-frequency band that gets masked by hats/snares.
    private static readonly float[] _laneGains  = [1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 2.5f, 1.0f, 1.0f];

    private readonly AL? _al;
    private readonly ALContext? _alc;
    private Device* _device;
    private Context* _context;

    internal AL? Al => _al;
    internal bool IsInitialized => _initialized;
    internal uint CreateStreamingSource() => _al!.GenSource();

    private readonly uint[] _buffers = new uint[LaneCount];
    private uint _hhOpenBuffer;
    private uint _rimshotBuffer;
    private uint _startupBuffer;
    private uint _startupSource;
    private uint _clickBuffer;
    private readonly uint[] _clickSources = new uint[2];
    private int _clickPoolIndex;
    private readonly uint[][] _sources;
    private bool _initialized;

    public AudioService()
    {
        _sources = new uint[LaneCount][];
        for (int i = 0; i < LaneCount; i++)
            _sources[i] = new uint[PoolSize];

        try
        {
            _al = AL.GetApi(soft: true);
            _alc = ALContext.GetApi(soft: true);
            Initialize();
        }
        catch
        {
            // Audio unavailable — app continues silently
        }
    }

    private void Initialize()
    {
        _device = _alc!.OpenDevice(null);
        if (_device == null) return;

        _context = _alc.CreateContext(_device, null);
        _alc.MakeContextCurrent(_context);

        string soundsDir = Path.Combine(AppContext.BaseDirectory, "Sounds");

        for (int lane = 0; lane < LaneCount; lane++)
        {
            _buffers[lane] = _al!.GenBuffer();

            var wav = WavLoader.TryLoad(Path.Combine(soundsDir, $"{_fileNames[lane]}.wav"));
            short[] samples = wav.HasValue ? wav.Value.Samples : DrumSynth.GenerateSamples(lane);
            int rate = wav.HasValue ? wav.Value.SampleRate : SampleRate;

            fixed (short* ptr = samples)
                _al.BufferData(_buffers[lane], BufferFormat.Mono16,
                    ptr, samples.Length * sizeof(short), rate);

            for (int j = 0; j < PoolSize; j++)
                _sources[lane][j] = _al.GenSource();
        }

        _hhOpenBuffer = _al!.GenBuffer();
        var hhOpen = WavLoader.TryLoad(Path.Combine(soundsDir, "hh_open.wav"));
        short[] openSamples = hhOpen.HasValue ? hhOpen.Value.Samples : DrumSynth.GenerateHiHatOpenSamples();
        int openRate = hhOpen.HasValue ? hhOpen.Value.SampleRate : SampleRate;
        fixed (short* ptr = openSamples)
            _al.BufferData(_hhOpenBuffer, BufferFormat.Mono16, ptr, openSamples.Length * sizeof(short), openRate);

        _rimshotBuffer = _al.GenBuffer();
        var rimshot = WavLoader.TryLoad(Path.Combine(soundsDir, "sn_rimshot.wav"));
        short[] rimSamples = rimshot.HasValue ? rimshot.Value.Samples : DrumSynth.GenerateRimshotSamples();
        int rimRate = rimshot.HasValue ? rimshot.Value.SampleRate : SampleRate;
        fixed (short* ptr = rimSamples)
            _al.BufferData(_rimshotBuffer, BufferFormat.Mono16, ptr, rimSamples.Length * sizeof(short), rimRate);

        _startupBuffer = _al.GenBuffer();
        var startup = WavLoader.TryLoad(Path.Combine(soundsDir, "rimshot.wav"));
        short[] startupSamples = startup.HasValue ? startup.Value.Samples : DrumSynth.GenerateRimshotSamples();
        int startupRate = startup.HasValue ? startup.Value.SampleRate : SampleRate;
        fixed (short* ptr = startupSamples)
            _al.BufferData(_startupBuffer, BufferFormat.Mono16, ptr, startupSamples.Length * sizeof(short), startupRate);
        _startupSource = _al.GenSource();

        _clickBuffer = _al.GenBuffer();
        var click = WavLoader.TryLoad(Path.Combine(soundsDir, "click.wav"));
        short[] clickSamples = click.HasValue ? click.Value.Samples : DrumSynth.GenerateClickSamples();
        int clickRate = click.HasValue ? click.Value.SampleRate : SampleRate;
        fixed (short* ptr = clickSamples)
            _al.BufferData(_clickBuffer, BufferFormat.Mono16, ptr, clickSamples.Length * sizeof(short), clickRate);
        for (int j = 0; j < 2; j++)
            _clickSources[j] = _al.GenSource();

        _initialized = true;
    }

    public void PlayStartup()
    {
        if (!_initialized) return;
        _al!.SourceStop(_startupSource);
        _al.SetSourceProperty(_startupSource, SourceInteger.Buffer, (int)_startupBuffer);
        _al.SetSourceProperty(_startupSource, SourceFloat.Gain, 0.85f);
        _al.SourcePlay(_startupSource);
    }

    public void PlayMetronomeClick()
    {
        if (!_initialized) return;
        uint src = _clickSources[_clickPoolIndex % 2];
        _clickPoolIndex++;
        _al!.SourceStop(src);
        _al.SetSourceProperty(src, SourceInteger.Buffer, (int)_clickBuffer);
        _al.SetSourceProperty(src, SourceFloat.Gain, 0.7f);
        _al.SourcePlay(src);
    }

    public void Play(int lane, int noteNumber, float gain)
    {
        if (!_initialized || lane < 0 || lane >= LaneCount) return;

        uint buf = (lane == 0 && noteNumber == 46) ? _hhOpenBuffer
                 : (lane == 2 && noteNumber == 37) ? _rimshotBuffer
                 : _buffers[lane];
        uint source = GetFreeSource(lane);
        _al!.SourceStop(source);
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buf);
        _al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(gain * _laneGains[lane], 0f, 3f));
        _al.SourcePlay(source);
    }

    private uint GetFreeSource(int lane)
    {
        foreach (uint src in _sources[lane])
        {
            _al!.GetSourceProperty(src, GetSourceInteger.SourceState, out int state);
            if (state != (int)SourceState.Playing)
                return src;
        }
        return _sources[lane][0]; // steal oldest
    }

    public void Dispose()
    {
        if (!_initialized) return;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            foreach (uint src in _sources[lane])
                _al!.DeleteSource(src);
            _al!.DeleteBuffer(_buffers[lane]);
        }
        _al!.DeleteBuffer(_hhOpenBuffer);
        _al!.DeleteBuffer(_rimshotBuffer);
        _al!.DeleteSource(_startupSource);
        _al!.DeleteBuffer(_startupBuffer);
        foreach (uint src in _clickSources)
            _al!.DeleteSource(src);
        _al!.DeleteBuffer(_clickBuffer);
        _alc!.MakeContextCurrent(null);
        _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        _al!.Dispose();
        _alc!.Dispose();
    }
}
