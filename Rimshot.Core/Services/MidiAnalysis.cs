using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Rimshot.Core.Models;

namespace Rimshot.Core.Services;

public sealed record MidiAnalysisResult(
    string FileName,
    string Format,
    int TrackCount,
    TimeSpan Duration,
    double PrimaryBpm,
    IReadOnlyList<TempoPoint> TempoChanges,
    IReadOnlyList<TimeSigPoint> TimeSignatures,
    int TotalNoteCount,
    IReadOnlyList<ChannelInfo> Channels,
    IReadOnlyList<RimshotLaneCoverage> KitCoverage,
    IReadOnlyList<UnmappedDrumNote> UnmappedDrums
);

public sealed record TempoPoint(TimeSpan Offset, double Bpm);

public sealed record TimeSigPoint(TimeSpan Offset, int Numerator, int Denominator);

public sealed record ChannelInfo(
    int ChannelNumber,
    int NoteCount,
    bool IsDrumChannel,
    IReadOnlyList<DrumInstrumentUsage>? DrumInstruments
);

public sealed record DrumInstrumentUsage(int NoteNumber, string InstrumentName, int HitCount);

public sealed record RimshotLaneCoverage(int LaneIndex, string Label, int HitCount);

public sealed record UnmappedDrumNote(int NoteNumber, string Name, int HitCount);

public static class MidiAnalysis
{
    public static MidiAnalysisResult Analyze(string filePath)
    {
        var midiFile = MidiFile.Read(filePath);
        var tempoMap = midiFile.GetTempoMap();

        string fileName   = Path.GetFileName(filePath);
        string format     = midiFile.OriginalFormat.ToString();
        int    trackCount = midiFile.Chunks.OfType<TrackChunk>().Count();

        var durationMetric = midiFile.GetDuration<MetricTimeSpan>();
        var duration       = TimeSpan.FromMilliseconds(durationMetric.TotalMicroseconds / 1000.0);

        var tempoChanges = tempoMap.GetTempoChanges()
            .Select(tc =>
            {
                var metric = TimeConverter.ConvertTo<MetricTimeSpan>(tc.Time, tempoMap);
                double bpm = 60_000_000.0 / tc.Value.MicrosecondsPerQuarterNote;
                return new TempoPoint(
                    TimeSpan.FromMilliseconds(metric.TotalMicroseconds / 1000.0),
                    bpm);
            })
            .ToList();

        double primaryBpm = tempoChanges.Count > 0 ? tempoChanges[0].Bpm : 120.0;

        var timeSignatures = tempoMap.GetTimeSignatureChanges()
            .Select(ts =>
            {
                var metric = TimeConverter.ConvertTo<MetricTimeSpan>(ts.Time, tempoMap);
                return new TimeSigPoint(
                    TimeSpan.FromMilliseconds(metric.TotalMicroseconds / 1000.0),
                    ts.Value.Numerator,
                    ts.Value.Denominator);
            })
            .ToList();

        var allNotes       = midiFile.GetNotes().ToList();
        int totalNoteCount = allNotes.Count;

        var channels = allNotes
            .GroupBy(n => (int)n.Channel)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                int  ch      = g.Key;
                bool isDrum  = ch == 9;
                int  count   = g.Count();

                IReadOnlyList<DrumInstrumentUsage>? drumInstruments = null;
                if (isDrum)
                {
                    drumInstruments = g
                        .GroupBy(n => (int)n.NoteNumber)
                        .Select(ng => new DrumInstrumentUsage(
                            ng.Key,
                            DrumMap.GetName(ng.Key),
                            ng.Count()))
                        .OrderByDescending(d => d.HitCount)
                        .ToList();
                }

                return new ChannelInfo(ch, count, isDrum, drumInstruments);
            })
            .ToList();

        var (kitCoverage, unmappedDrums) = BuildKitCoverage(channels);

        return new MidiAnalysisResult(
            fileName,
            format,
            trackCount,
            duration,
            primaryBpm,
            tempoChanges,
            timeSignatures,
            totalNoteCount,
            channels,
            kitCoverage,
            unmappedDrums);
    }

    private static (IReadOnlyList<RimshotLaneCoverage>, IReadOnlyList<UnmappedDrumNote>)
        BuildKitCoverage(IReadOnlyList<ChannelInfo> channels)
    {
        var lanes      = DrumLane.StandardKit();
        var laneByNote = new Dictionary<int, int>();
        foreach (var lane in lanes)
            foreach (int note in lane.NoteNumbers)
                laneByNote[note] = lane.Index;

        var laneHits = new int[lanes.Count];
        var unmapped = new List<UnmappedDrumNote>();

        var drumChannel = channels.FirstOrDefault(c => c.IsDrumChannel);
        if (drumChannel?.DrumInstruments is { } drums)
        {
            foreach (var d in drums)
            {
                if (laneByNote.TryGetValue(d.NoteNumber, out int laneIdx))
                    laneHits[laneIdx] += d.HitCount;
                else
                    unmapped.Add(new UnmappedDrumNote(d.NoteNumber, d.InstrumentName, d.HitCount));
            }
        }

        var coverage = lanes
            .Select(l => new RimshotLaneCoverage(l.Index, l.Label, laneHits[l.Index]))
            .ToList();

        return (coverage, unmapped);
    }
}
