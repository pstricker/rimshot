using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Rimshot.Core.Models;

namespace Rimshot.Core.Services;

public static class SongLoader
{
    private static readonly Dictionary<int, int> _noteToLane = BuildNoteToLaneMap();

    // GM CCs we keep. Anything else is dropped to avoid bloating the event stream
    // and confusing the synth with edits/RPN/NRPN messages we don't model.
    private static readonly HashSet<int> _interestingCCs = new() { 7, 10, 11, 64 };

    public static Song Load(string filePath) => LoadWithBpm(filePath).Song;

    public static (Song Song, double OriginalBpm) LoadWithBpm(string filePath)
    {
        var midiFile = MidiFile.Read(filePath);
        var tempoMap = midiFile.GetTempoMap();

        double originalBpm = 120.0;
        var tempoChanges = tempoMap.GetTempoChanges().ToList();
        if (tempoChanges.Count > 0)
            originalBpm = 60_000_000.0 / tempoChanges[0].Value.MicrosecondsPerQuarterNote;

        double secondsPerQuarter = 60.0 / originalBpm;

        var allNotes = midiFile.GetNotes().ToList();

        var drumNotes = allNotes
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

        var melodicNotes = allNotes
            .Where(n => (int)n.Channel != 9)
            .Select(n =>
            {
                long startMicros = n.TimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds;
                long endMicros   = n.EndTimeAs<MetricTimeSpan>(tempoMap).TotalMicroseconds;
                double startEighths = (startMicros / 1_000_000.0) / secondsPerQuarter * 2.0;
                double endEighths   = (endMicros   / 1_000_000.0) / secondsPerQuarter * 2.0;
                double durationEighths = Math.Max(0.05, endEighths - startEighths);
                return new MelodicNote(
                    startEighths,
                    (int)n.Channel,
                    (int)n.NoteNumber,
                    n.Velocity,
                    durationEighths);
            })
            .OrderBy(n => n.OffsetInEighths)
            .ToArray();

        var controlEvents = new List<MelodicEvent>();
        foreach (var chunk in midiFile.GetTrackChunks())
        {
            foreach (var te in chunk.GetTimedEvents())
            {
                long micros = TimeConverter.ConvertTo<MetricTimeSpan>(te.Time, tempoMap).TotalMicroseconds;
                double offset = (micros / 1_000_000.0) / secondsPerQuarter * 2.0;

                switch (te.Event)
                {
                    case ProgramChangeEvent pc when (int)pc.Channel != 9:
                        controlEvents.Add(new ProgramChange(offset, (int)pc.Channel, (int)(SevenBitNumber)pc.ProgramNumber));
                        break;
                    case ControlChangeEvent cc when (int)cc.Channel != 9:
                        int controllerId = (int)(SevenBitNumber)cc.ControlNumber;
                        if (_interestingCCs.Contains(controllerId))
                            controlEvents.Add(new ControlChange(offset, (int)cc.Channel, controllerId, (int)(SevenBitNumber)cc.ControlValue));
                        break;
                    case PitchBendEvent pb when (int)pb.Channel != 9:
                        controlEvents.Add(new PitchBend(offset, (int)pb.Channel, pb.PitchValue));
                        break;
                }
            }
        }

        controlEvents.Sort((a, b) => a.OffsetInEighths.CompareTo(b.OffsetInEighths));

        MelodicTrack? melodic = melodicNotes.Length > 0
            ? new MelodicTrack(melodicNotes, controlEvents.ToArray())
            : null;

        double durationMicros = midiFile.GetDuration<MetricTimeSpan>().TotalMicroseconds;
        double totalEighths = (durationMicros / 1_000_000.0) / secondsPerQuarter * 2.0;
        double roundedTotal = Math.Max(8.0, Math.Ceiling(totalEighths / 8.0) * 8.0);

        string name = Path.GetFileNameWithoutExtension(filePath);
        var song = new Song(name, drumNotes, roundedTotal, ShouldLoop: false)
        {
            MelodicTrack = melodic,
        };
        return (song, originalBpm);
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
