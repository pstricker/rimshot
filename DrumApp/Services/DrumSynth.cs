using System;

namespace DrumApp.Services;

public static class DrumSynth
{
    private const int SampleRate = 44100;
    private static readonly Random _rng = new(42);

    public static short[] GenerateHiHatOpenSamples() =>
        Noise(0.400, decayRate: 6);

    public static short[] GenerateRimshotSamples() =>
        NoisePlusTone(0.060, freq: 600, decayRate: 40); // sharp, high transient

    public static short[] GenerateClickSamples() =>
        Tone(0.020, freq: 1200, decayRate: 250); // very short metronome click

    public static short[] GenerateSamples(int lane) => lane switch
    {
        0 => Noise(0.080, decayRate: 40),                    // HH
        1 => Noise(0.500, decayRate: 4),                     // CR
        2 => NoisePlusTone(0.120, freq: 220, decayRate: 25), // SN
        3 => Tone(0.250, freq: 250, decayRate: 14),          // TM1 high
        4 => Tone(0.280, freq: 210, decayRate: 12),          // TM2 mid
        5 => BassDrum(0.350),                                // BD
        6 => Tone(0.300, freq: 180, decayRate: 10),          // FTM
        7 => Noise(0.700, decayRate: 2.5),                   // RD
        _ => Noise(0.100, decayRate: 30),
    };

    private static short[] Noise(double durationSec, double decayRate)
    {
        int n = (int)(SampleRate * durationSec);
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double s = (_rng.NextDouble() * 2 - 1) * Math.Exp(-t * decayRate);
            pcm[i] = ToShort(s);
        }
        return pcm;
    }

    private static short[] Tone(double durationSec, double freq, double decayRate)
    {
        int n = (int)(SampleRate * durationSec);
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double s = Math.Sin(2 * Math.PI * freq * t) * Math.Exp(-t * decayRate);
            pcm[i] = ToShort(s);
        }
        return pcm;
    }

    private static short[] NoisePlusTone(double durationSec, double freq, double decayRate)
    {
        int n = (int)(SampleRate * durationSec);
        var pcm = new short[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double env = Math.Exp(-t * decayRate);
            double s = ((_rng.NextDouble() * 2 - 1) * 0.6 + Math.Sin(2 * Math.PI * freq * t) * 0.4) * env;
            pcm[i] = ToShort(s);
        }
        return pcm;
    }

    private static short[] BassDrum(double durationSec)
    {
        int n = (int)(SampleRate * durationSec);
        var pcm = new short[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double freq = 60 + 80 * Math.Exp(-t * 20); // pitch drops 140→60 Hz
            phase += 2 * Math.PI * freq / SampleRate;
            double s = Math.Sin(phase) * Math.Exp(-t * 9);
            pcm[i] = ToShort(s);
        }
        return pcm;
    }

    private static short ToShort(double s) =>
        (short)(Math.Clamp(s, -1.0, 1.0) * 32767);
}
