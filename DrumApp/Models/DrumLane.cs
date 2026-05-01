using System.Collections.Generic;
using Avalonia.Media;

namespace DrumApp.Models;

public record DrumLane(int Index, string Label, int[] NoteNumbers, Color Color)
{
    public static IReadOnlyList<DrumLane> StandardKit() =>
    [
        new(0, "HH", [42, 44, 46], Color.Parse("#FFD700")),
        new(1, "CR", [49],     Color.Parse("#FF7F50")),
        new(2, "SN", [38, 37], Color.Parse("#FF6347")),
        new(3, "TM", [48],     Color.Parse("#6495ED")),
        new(4, "BD", [36],     Color.Parse("#9370DB")),
        new(5, "TM", [41, 43], Color.Parse("#4682B4")),
        new(6, "RD", [51],     Color.Parse("#66CDAA")),
    ];
}
