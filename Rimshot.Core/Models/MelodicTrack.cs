namespace Rimshot.Core.Models;

// OffsetInEighths matches PatternNote: 1.0 = eighth note. Channel is 0-based (0..15).
public record MelodicNote(
    double OffsetInEighths,
    int Channel,
    int NoteNumber,
    int Velocity,
    double DurationInEighths
);

public abstract record MelodicEvent(double OffsetInEighths, int Channel);
public record ProgramChange(double OffsetInEighths, int Channel, int Program)
    : MelodicEvent(OffsetInEighths, Channel);
public record ControlChange(double OffsetInEighths, int Channel, int Controller, int Value)
    : MelodicEvent(OffsetInEighths, Channel);
// 14-bit value, 0..16383, center 8192.
public record PitchBend(double OffsetInEighths, int Channel, int Value)
    : MelodicEvent(OffsetInEighths, Channel);

// Both arrays sorted ascending by OffsetInEighths.
public record MelodicTrack(MelodicNote[] Notes, MelodicEvent[] ControlEvents)
{
    public bool HasContent => Notes.Length > 0;
}
