namespace Rimshot.Core.Models;

// OffsetInEighths measured from pattern start: 1.0 = eighth note, 0.5 = 16th, 0.25 = 32nd
public record PatternNote(double OffsetInEighths, int Lane);
