using System;

namespace Rimshot.Core.Services;

/// <summary>
/// Tracks the user-selected practice-loop region in eighth-note coordinates.
/// All inputs are snapped to the 1/16 grid (0.5 eighths). Minimum loop length
/// is 0.5 eighths (one 1/16). Listeners observe <see cref="LoopChanged"/> for
/// any state transition (set / refine / clear).
/// </summary>
public sealed class LoopSelectionService
{
    /// <summary>1/16 note = half an eighth. Single source of grid resolution.</summary>
    public const double GridStepEighths = 0.5;

    /// <summary>Minimum loop length: one 1/16 cell.</summary>
    public const double MinLengthEighths = GridStepEighths;

    public bool   IsActive     { get; private set; }
    public double StartEighths { get; private set; }
    public double EndEighths   { get; private set; }

    /// <summary>Total length of the host song, used to clamp range. 0 disables.</summary>
    public double SongLengthEighths { get; private set; }

    public event EventHandler? LoopChanged;

    public void SetSongLength(double totalEighths)
    {
        SongLengthEighths = Math.Max(0, totalEighths);
        if (IsActive)
        {
            // Re-clamp existing loop to new song length (and clear if it no longer fits).
            double maxEnd = SongLengthEighths;
            if (StartEighths >= maxEnd - MinLengthEighths + 1e-6)
            {
                ClearLoop();
                return;
            }
            double newEnd = Math.Min(EndEighths, maxEnd);
            if (Math.Abs(newEnd - EndEighths) > 1e-9)
            {
                EndEighths = newEnd;
                LoopChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Snaps a raw eighth value to the 1/16 grid.
    /// </summary>
    public static double SnapToGrid(double eighths) =>
        Math.Round(eighths * 2.0) / 2.0;

    /// <summary>
    /// Sets the loop range, snapping to the 1/16 grid and enforcing the
    /// minimum length and song-length bounds. Order-agnostic: caller may pass
    /// start/end in any order. Returns false if the request couldn't fit.
    /// </summary>
    public bool SetRange(double startEighths, double endEighths)
    {
        if (SongLengthEighths <= 0) return false;

        double a = SnapToGrid(Math.Min(startEighths, endEighths));
        double b = SnapToGrid(Math.Max(startEighths, endEighths));

        // Clamp to song bounds
        a = Math.Clamp(a, 0, SongLengthEighths - MinLengthEighths);
        b = Math.Clamp(b, MinLengthEighths, SongLengthEighths);

        // Enforce min length
        if (b - a < MinLengthEighths)
        {
            // Prefer extending end forward; fall back to pulling start back.
            b = a + MinLengthEighths;
            if (b > SongLengthEighths)
            {
                b = SongLengthEighths;
                a = b - MinLengthEighths;
            }
        }

        if (IsActive
            && Math.Abs(a - StartEighths) < 1e-9
            && Math.Abs(b - EndEighths)   < 1e-9)
        {
            return true; // unchanged
        }

        IsActive     = true;
        StartEighths = a;
        EndEighths   = b;
        LoopChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Drags the start handle to <paramref name="newStartEighths"/>; clamped so
    /// it can't cross the end handle (min 1/16 gap preserved).
    /// </summary>
    public bool MoveStart(double newStartEighths)
    {
        if (!IsActive) return false;
        double snapped = SnapToGrid(newStartEighths);
        snapped = Math.Clamp(snapped, 0, EndEighths - MinLengthEighths);
        if (Math.Abs(snapped - StartEighths) < 1e-9) return false;
        StartEighths = snapped;
        LoopChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Drags the end handle to <paramref name="newEndEighths"/>; clamped so it
    /// can't cross the start handle (min 1/16 gap preserved).
    /// </summary>
    public bool MoveEnd(double newEndEighths)
    {
        if (!IsActive) return false;
        double snapped = SnapToGrid(newEndEighths);
        snapped = Math.Clamp(snapped, StartEighths + MinLengthEighths, SongLengthEighths);
        if (Math.Abs(snapped - EndEighths) < 1e-9) return false;
        EndEighths = snapped;
        LoopChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ClearLoop()
    {
        if (!IsActive) return;
        IsActive     = false;
        StartEighths = 0;
        EndEighths   = 0;
        LoopChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True if <paramref name="eighths"/> sits within the active loop.</summary>
    public bool Contains(double eighths) =>
        IsActive
        && eighths >= StartEighths - 1e-9
        && eighths <  EndEighths   - 1e-9;
}
