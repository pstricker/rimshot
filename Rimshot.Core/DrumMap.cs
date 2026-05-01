using System.Collections.Generic;

namespace Rimshot.Core;

public static class DrumMap
{
    private static readonly Dictionary<int, string> _map = new()
    {
        { 35, "Bass Drum 2" },
        { 36, "Bass Drum 1" },
        { 37, "Side Stick" },
        { 38, "Snare" },
        { 39, "Hand Clap" },
        { 40, "Snare (Rim)" },
        { 41, "Low Floor Tom" },
        { 42, "Closed Hi-Hat" },
        { 43, "High Floor Tom" },
        { 44, "Hi-Hat Pedal" },
        { 45, "Low Tom" },
        { 46, "Open Hi-Hat" },
        { 47, "Low-Mid Tom" },
        { 48, "Hi-Mid Tom" },
        { 49, "Crash 1" },
        { 50, "High Tom" },
        { 51, "Ride 1" },
        { 52, "Chinese Cymbal" },
        { 53, "Ride Bell" },
        { 54, "Tambourine" },
        { 55, "Splash Cymbal" },
        { 56, "Cowbell" },
        { 57, "Crash 2" },
        { 58, "Vibraslap" },
        { 59, "Ride 2" },
    };

    public static string GetName(int noteNumber) =>
        _map.TryGetValue(noteNumber, out var name) ? name : $"Note {noteNumber}";
}
