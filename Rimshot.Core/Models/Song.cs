namespace Rimshot.Core.Models;

public record Song(
    string Name,
    PatternNote[] Notes,    // sorted by OffsetInEighths ascending
    double TotalEighths,    // loop/end boundary; 8.0 = one 4/4 bar
    bool ShouldLoop
);
