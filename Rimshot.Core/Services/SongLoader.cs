using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Rimshot.Core.Models;

namespace Rimshot.Core.Services;

public static class SongLoader
{
    private static readonly Dictionary<int, int> _noteToLane = BuildNoteToLaneMap();

    public static Song Load(string filePath)
    {
        var midiFile = MidiFile.Read(filePath);
        var tempoMap = midiFile.GetTempoMap();

        double originalBpm = 120.0;
        var tempoChanges = tempoMap.GetTempoChanges().ToList();
        if (tempoChanges.Count > 0)
            originalBpm = 60_000_000.0 / tempoChanges[0].Value.MicrosecondsPerQuarterNote;

        double secondsPerQuarter = 60.0 / originalBpm;

        var notes = midiFile.GetNotes()
            .Where(n => (int)n.Channel == 9)
            .Select(n =>
            {
                long micros = n.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds;
                double offsetInEighths = (micros / 1_000_000.0) / secondsPerQuarter * 2.0;
                int lane = _noteToLane.TryGetValue((int)n.NoteNumber, out int l) ? l : -1;
                return (offsetInEighths, lane);
            })
            .Where(x => x.lane >= 0)
            .OrderBy(x => x.offsetInEighths)
            .Select(x => new PatternNote(x.offsetInEighths, x.lane))
            .ToArray();

        double durationMicros = midiFile.GetDuration<MetricTimeSpan>().TotalMicroseconds;
        double totalEighths = (durationMicros / 1_000_000.0) / secondsPerQuarter * 2.0;
        double roundedTotal = Math.Max(8.0, Math.Ceiling(totalEighths / 8.0) * 8.0);

        string name = Path.GetFileNameWithoutExtension(filePath);
        return new Song(name, notes, roundedTotal, ShouldLoop: false);
    }

    private static Dictionary<int, int> BuildNoteToLaneMap()
    {
        var map = new Dictionary<int, int>();
        foreach (var lane in DrumLane.StandardKit())
            foreach (int note in lane.NoteNumbers)
                map[note] = lane.Index;
        return map;
    }
}
