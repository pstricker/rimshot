using System.Collections.Generic;
using System.Linq;
using Rimshot.Core.Models;

namespace Rimshot.Services;

// Lane indices match DrumLane.StandardKit(): HH=0, CR=1, SN=2, TM1=3, TM2=4, BD=5, FTM=6, RD=7
//
// All 40 PAS rudiments are encoded below. Sticking strings follow the convention
// of RudimentBuilder: uppercase R/L = main stroke, lowercase r/l = grace note(s)
// preceding the next main stroke (one grace = flam, two graces = drag).
//
// Subdivisions: 0.25 = 16th, 0.5 = 8th, 1.0 = quarter, 0.125 = 32nd, 1.0/3 = 8th-triplet.
// TotalEighths: the loop boundary. Patterns whose natural cycle isn't a whole bar
// loop on a non-bar boundary — that's fine for repetitive practice.
public static class SongLibrary
{
    public static readonly IReadOnlyList<BuiltInGroup> Groups = BuildGroups();

    public static readonly IReadOnlyList<Song> BuiltIn =
        Groups.SelectMany(g => g.Songs).ToList();

    private static IReadOnlyList<BuiltInGroup> BuildGroups()
    {
        var b = RudimentBuilder.FromFlamSticking;

        return
        [
            new BuiltInGroup("Roll Rudiments",
            [
                b("Single Stroke Roll",        "RLRL",                            0.25,    2, 8.0),
                b("Single Stroke Four",        "RLRL LRLR",                       0.25,    2, 2.0),
                b("Single Stroke Seven",       "RLRLRLR LRLRLRL",                 0.25,    2, 7.0),
                b("Multiple Bounce Roll",      "RLRL",                            0.125,   2, 8.0),
                b("Triple Stroke Roll",        "RRRLLL",                          0.25,    2, 6.0),
                b("Double Stroke Open Roll",   "RRLL",                            0.25,    2, 8.0),
                b("Five Stroke Roll",          "RRLLR LLRRL",                     0.25,    2, 5.0),
                b("Six Stroke Roll",           "RLLRRL LRRLLR",                   0.25,    2, 6.0),
                b("Seven Stroke Roll",         "RRLLRRL LLRRLLR",                 0.25,    2, 7.0),
                b("Nine Stroke Roll",          "RRLLRRLLR LLRRLLRRL",             0.25,    2, 9.0),
                b("Ten Stroke Roll",           "RRLLRRLLRL LLRRLLRRLR",           0.25,    2, 10.0),
                b("Eleven Stroke Roll",        "RRLLRRLLRRL LLRRLLRRLLR",         0.25,    2, 11.0),
                b("Thirteen Stroke Roll",      "RRLLRRLLRRLLR LLRRLLRRLLRRL",     0.25,    2, 13.0),
                b("Fifteen Stroke Roll",       "RRLLRRLLRRLLRRL LLRRLLRRLLRRLLR", 0.25,    2, 15.0),
                b("Seventeen Stroke Roll",     "RRLLRRLLRRLLRRLLR LLRRLLRRLLRRLLRRL", 0.25, 2, 17.0),
            ]),

            new BuiltInGroup("Diddle Rudiments",
            [
                b("Single Paradiddle",         "RLRR LRLL",                       0.25,    2, 4.0),
                b("Double Paradiddle",         "RLRLRR LRLRLL",                   0.25,    2, 6.0),
                b("Triple Paradiddle",         "RLRLRLRR LRLRLRLL",               0.25,    2, 8.0),
                b("Single Paradiddle-Diddle",  "RLRRLL",                          0.25,    2, 6.0),
            ]),

            new BuiltInGroup("Flam Rudiments",
            [
                b("Flam",                      "lR rL",                           1.0,     2, 4.0),
                b("Flam Accent",               "lR L L rL R R",                   1.0/3,   2, 4.0),
                b("Flam Tap",                  "lR R rL L",                       0.25,    2, 2.0),
                b("Flamacue",                  "lR L L rL rL R R lR",             0.25,    2, 4.0),
                b("Flam Paradiddle",           "lRLRR rLRLL",                     0.25,    2, 4.0),
                b("Single Flammed Mill",       "lRLR rLRL",                       0.25,    2, 2.0),
                b("Flam Paradiddle-Diddle",    "lRLRRLL rLRLLRR",                 0.25,    2, 7.0/2),
                b("Pataflafla",                "lR L L rL rL R R lR",             0.25,    2, 4.0),
                b("Swiss Army Triplet",        "lRLR rLRL",                       1.0/3,   2, 4.0),
                b("Inverted Flam Tap",         "lR L rL R",                       0.25,    2, 1.0),
                b("Flam Drag",                 "lR llR rL rrL",                   0.25,    2, 1.0),
            ]),

            new BuiltInGroup("Drag Rudiments",
            [
                b("Drag",                      "llR rrL",                         0.5,     2, 2.0),
                b("Single Drag Tap",           "llR R rrL L",                     0.25,    2, 1.0),
                b("Double Drag Tap",           "llR R R rrL L L",                 0.25,    2, 1.5),
                b("Lesson 25",                 "llR L R rrL R L",                 0.25,    2, 2.0),
                b("Single Dragadiddle",        "llRLRR rrLRLL",                   0.25,    2, 3.0),
                b("Drag Paradiddle #1",        "R llRLRR L rrLRLL",               0.25,    2, 5.0/2),
                b("Drag Paradiddle #2",        "R R llRLRR L L rrLRLL",           0.25,    2, 3.0),
                b("Single Ratamacue",          "llR L R L rrL R L R",             0.25,    2, 2.0),
                b("Double Ratamacue",          "llR llR L R L rrL rrL R L R",     0.25,    2, 5.0/2),
                b("Triple Ratamacue",          "llR llR llR L R L rrL rrL rrL R L R", 0.25, 2, 3.0),
            ]),

            new BuiltInGroup("Grooves",
            [
                new Song("Rock Beat",
                    Notes:
                    [
                        new(0.0, 0), new(0.0, 5), new(0.0, 1), // beat 1: HH + BD + Crash
                        new(1.0, 0),                           // & 1: HH
                        new(2.0, 0), new(2.0, 2),              // beat 2: HH + SN
                        new(3.0, 0),                           // & 2: HH
                        new(4.0, 0), new(4.0, 5),              // beat 3: HH + BD
                        new(5.0, 0),                           // & 3: HH
                        new(6.0, 0), new(6.0, 2),              // beat 4: HH + SN
                        new(7.0, 0),                           // & 4: HH
                    ],
                    TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),

                new Song("4-on-the-Floor",
                    Notes:
                    [
                        new(0.0, 0), new(0.0, 5),              // HH + BD
                        new(1.0, 0),                           // HH
                        new(2.0, 0), new(2.0, 2),              // HH + SN
                        new(3.0, 0),                           // HH
                        new(4.0, 0), new(4.0, 5),              // HH + BD
                        new(5.0, 0),                           // HH
                        new(6.0, 0), new(6.0, 2), new(6.0, 5), // HH + SN + BD
                        new(7.0, 0),                           // HH
                    ],
                    TotalEighths: 8.0, ShouldLoop: true, IntroEighths: 8.0),
            ]),
        ];
    }
}

public record BuiltInGroup(string Title, IReadOnlyList<Song> Songs);
