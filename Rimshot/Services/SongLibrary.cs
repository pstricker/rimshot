using System.Collections.Generic;
using Rimshot.Core.Models;

namespace Rimshot.Services;

// Lane indices match DrumLane.StandardKit(): HH=0, CR=1, SN=2, TM1=3, TM2=4, BD=5, FTM=6, RD=7
public static class SongLibrary
{
    public static readonly Song LoadFromFile = new("Load from file…", [], 0, false);

    public static readonly IReadOnlyList<Song> BuiltIn =
    [
        new Song("Rock Beat",
            Notes:
            [
                new(0.0, 0), new(0.0, 5), new(0.0, 1), // beat 1: HH + BD + Crash
                new(1.0, 0),                             // & 1: HH
                new(2.0, 0), new(2.0, 2),               // beat 2: HH + SN
                new(3.0, 0),                             // & 2: HH
                new(4.0, 0), new(4.0, 5),               // beat 3: HH + BD
                new(5.0, 0),                             // & 3: HH
                new(6.0, 0), new(6.0, 2),               // beat 4: HH + SN
                new(7.0, 0),                             // & 4: HH
            ],
            TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),

        new Song("Single Stroke Roll",
            Notes:
            [
                new(0.0, 2), new(0.5, 2), new(1.0, 2), new(1.5, 2),
                new(2.0, 2), new(2.5, 2), new(3.0, 2), new(3.5, 2),
            ],
            TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),

        new Song("Double Stroke Roll",
            Notes:
            [
                new(0.0, 2), new(0.25, 2),
                new(1.0, 2), new(1.25, 2),
                new(2.0, 2), new(2.25, 2),
                new(3.0, 2), new(3.25, 2),
                new(4.0, 2), new(4.25, 2),
                new(5.0, 2), new(5.25, 2),
                new(6.0, 2), new(6.25, 2),
                new(7.0, 2), new(7.25, 2),
            ],
            TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),

        // RLRR LRLL — accented hits on TM1, unaccented on SN
        new Song("Paradiddle",
            Notes:
            [
                new(0.0, 3),  // R (accent)
                new(0.5, 2),  // L
                new(1.0, 3),  // R (accent)
                new(1.5, 3),  // R
                new(2.0, 2),  // L (accent)
                new(2.5, 3),  // R
                new(3.0, 2),  // L (accent)
                new(3.5, 2),  // L
                new(4.0, 3),  // R (accent)
                new(4.5, 2),  // L
                new(5.0, 3),  // R (accent)
                new(5.5, 3),  // R
                new(6.0, 2),  // L (accent)
                new(6.5, 3),  // R
                new(7.0, 2),  // L (accent)
                new(7.5, 2),  // L
            ],
            TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),

        new Song("4-on-the-Floor",
            Notes:
            [
                new(0.0, 0), new(0.0, 5),               // HH + BD
                new(1.0, 0),                             // HH
                new(2.0, 0), new(2.0, 2),               // HH + SN
                new(3.0, 0),                             // HH
                new(4.0, 0), new(4.0, 5),               // HH + BD
                new(5.0, 0),                             // HH
                new(6.0, 0), new(6.0, 2), new(6.0, 5), // HH + SN + BD
                new(7.0, 0),                             // HH
            ],
            TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),
    ];

    public static IReadOnlyList<Song> AllItems => [..BuiltIn, LoadFromFile];
}
