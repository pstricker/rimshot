using System;

namespace Rimshot.Core.Models;

public record DrumHit(DateTime Timestamp, string DrumName, int NoteNumber, int Velocity)
{
    public string Display =>
        $"[{Timestamp:HH:mm:ss.fff}]  {DrumName,-20}  vel: {Velocity}";
}
