using System;
using System.Collections.Generic;
using Rimshot.Core.Models;

namespace Rimshot.Services;

public static class RudimentBuilder
{
    // Each grace note sits a 32nd ahead of the next grace (or main stroke).
    // For 1 grace (flam), offset = -1/32 eighth = -0.0625.
    // For 2 graces (drag), offsets = -0.125 and -0.0625.
    private const double GraceSpacingEighths = 0.0625;

    /// <summary>
    /// Builds a Song from a sticking string of upper-case stroke letters (R/L),
    /// repeating to fill <paramref name="totalEighths"/>. Spaces, dashes, and
    /// pipes are ignored as visual grouping.
    /// </summary>
    public static Song FromSticking(
        string name,
        string sticking,
        double subdivision = 0.25,
        int lane = 2,
        double totalEighths = 8.0,
        double introEighths = 8.0)
    {
        return FromFlamSticking(name, sticking, subdivision, lane, totalEighths, introEighths);
    }

    /// <summary>
    /// Builds a Song from a sticking string that supports flams and drags.
    /// Each "slot" is a sequence of zero or more grace notes (lowercase r/l)
    /// followed by exactly one main stroke (uppercase R/L). Slots are placed
    /// at <paramref name="subdivision"/> intervals; graces precede the slot's
    /// main stroke by a 32nd note (flam) or two packed 32nds (drag).
    ///
    /// Example: "lR rL lR rL" — 4 alternating flams (one per slot).
    /// Example: "llR R rrL L" — single drag tap (drag + tap, alternating).
    /// </summary>
    public static Song FromFlamSticking(
        string name,
        string pattern,
        double subdivision = 0.25,
        int lane = 2,
        double totalEighths = 8.0,
        double introEighths = 8.0)
    {
        var slots = ParseSlots(pattern);
        if (slots.Count == 0) return new Song(name, [], totalEighths, ShouldLoop: true, IntroEighths: introEighths);

        var notes = new List<PatternNote>();
        int slotIdx = 0;
        while (slotIdx * subdivision < totalEighths - 1e-9)
        {
            var slot = slots[slotIdx % slots.Count];
            double slotOffset = slotIdx * subdivision;

            // Place graces immediately before the slot's main stroke.
            for (int g = 0; g < slot.Graces.Length; g++)
            {
                // For N graces, packed: offsets at -N*GraceSpacing ... -1*GraceSpacing
                double graceOff = slotOffset + (g - slot.Graces.Length) * GraceSpacingEighths;
                if (graceOff < 0) graceOff += totalEighths; // wrap pre-loop graces to end
                notes.Add(new PatternNote(graceOff, lane, LetterToHand(slot.Graces[g])));
            }

            // Place main stroke
            notes.Add(new PatternNote(slotOffset, lane, LetterToHand(slot.Main)));
            slotIdx++;
        }

        notes.Sort((a, b) => a.OffsetInEighths.CompareTo(b.OffsetInEighths));
        return new Song(name, notes.ToArray(), totalEighths, ShouldLoop: true, IntroEighths: introEighths);
    }

    private static Hand LetterToHand(char c) => c is 'R' or 'r' ? Hand.Right : Hand.Left;

    private readonly record struct Slot(string Graces, char Main);

    private static List<Slot> ParseSlots(string pattern)
    {
        var slots = new List<Slot>();
        var graces = new System.Text.StringBuilder();
        foreach (char c in pattern)
        {
            if (c is 'r' or 'l')
                graces.Append(c);
            else if (c is 'R' or 'L')
            {
                slots.Add(new Slot(graces.ToString(), c));
                graces.Clear();
            }
            // any other char (space, dash, pipe, etc.) is grouping noise — ignore
        }
        return slots;
    }

}
