using System;
using System.IO;
using System.Text;

namespace Rimshot.Services;

public static class WavLoader
{
    public static (short[] Samples, int SampleRate)? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));

            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return null;
            reader.ReadInt32(); // file size
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return null;

            int channels = 1, sampleRate = 44100, bitsPerSample = 16;
            byte[]? audioData = null;

            while (reader.BaseStream.Position <= reader.BaseStream.Length - 8)
            {
                string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                int chunkSize = reader.ReadInt32();

                if (chunkId == "fmt ")
                {
                    if (reader.ReadInt16() != 1) return null; // PCM only
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // byte rate
                    reader.ReadInt16(); // block align
                    bitsPerSample = reader.ReadInt16();
                    if (chunkSize > 16) reader.ReadBytes(chunkSize - 16);
                }
                else if (chunkId == "data")
                {
                    audioData = reader.ReadBytes(chunkSize);
                    break;
                }
                else
                {
                    if (chunkSize > 0) reader.ReadBytes(chunkSize);
                }
            }

            if (audioData == null || (bitsPerSample != 16 && bitsPerSample != 24)) return null;

            int bytesPerSample = bitsPerSample / 8;
            int frameCount = audioData.Length / (bytesPerSample * channels);
            var samples = new short[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                int sum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int offset = (i * channels + ch) * bytesPerSample;
                    if (bitsPerSample == 24)
                    {
                        int s24 = audioData[offset] | (audioData[offset + 1] << 8) | ((sbyte)audioData[offset + 2] << 16);
                        sum += s24 >> 8; // scale to 16-bit range
                    }
                    else
                    {
                        sum += BitConverter.ToInt16(audioData, offset);
                    }
                }
                samples[i] = (short)(sum / channels);
            }

            return (samples, sampleRate);
        }
        catch { return null; }
    }
}
