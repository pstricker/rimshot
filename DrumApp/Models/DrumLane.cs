using System.Collections.Generic;
using Avalonia.Media;

namespace DrumApp.Models;

public record DrumLane(int Index, string Label, int[] NoteNumbers, Color Color)
{
    public static IReadOnlyList<DrumLane> StandardKit() =>
    [
        new(0, "HH",  [42, 44, 46], Color.Parse("#C5432F")), // red
        new(1, "CR",  [49],         Color.Parse("#D99515")), // amber
        new(2, "SN",  [38, 37],     Color.Parse("#5E7D1A")), // olive
        new(3, "TM1", [50, 48],     Color.Parse("#D99515")), // amber
        new(4, "TM2", [47],         Color.Parse("#D99515")), // amber
        new(5, "BD",  [36],         Color.Parse("#C5432F")), // red
        new(6, "FTM", [41, 43],     Color.Parse("#D99515")), // amber
        new(7, "RD",  [51],         Color.Parse("#5E7D1A")), // olive
    ];
}
