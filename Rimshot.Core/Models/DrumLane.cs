using System.Collections.Generic;
using Avalonia.Media;

namespace Rimshot.Core.Models;

public record DrumLane(int Index, string Label, int[] NoteNumbers, Color Color)
{
    public static IReadOnlyList<DrumLane> StandardKit() =>
    [
        new(0, "HH",  [42, 44, 46], Color.Parse("#FFD700")), // cymbal — yellow
        new(1, "CR",  [49],         Color.Parse("#FFD700")), // cymbal — yellow
        new(2, "SN",  [38, 37],     Color.Parse("#FF1493")), // drum   — hot pink
        new(3, "TM1", [50, 48],     Color.Parse("#00D9FF")), // drum   — cyan
        new(4, "TM2", [47],         Color.Parse("#00D9FF")), // drum   — cyan
        new(5, "BD",  [36],         Color.Parse("#FF1493")), // drum   — hot pink
        new(6, "FTM", [41, 43],     Color.Parse("#00D9FF")), // drum   — cyan
        new(7, "RD",  [51],         Color.Parse("#FFD700")), // cymbal — yellow
    ];
}
