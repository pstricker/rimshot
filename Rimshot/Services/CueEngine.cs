using System;
using System.Collections.Generic;
using Rimshot.Core.Models;
using Rimshot.Core.Services;

namespace Rimshot.Services;

public enum CueEngineState { Stopped, Running, Paused }

public record MelodicCue(DateTime ScheduledAt, int Channel, byte Command, byte Data1, byte Data2);

public class CueEngine
{
    private readonly LoopSelectionService? _loop;

    public CueEngine() { }

    public CueEngine(LoopSelectionService loop)
    {
        _loop = loop;
        _loop.LoopChanged += OnLoopChanged;
    }

    private int _bpm = 90;
    public int Bpm
    {
        get => _bpm;
        set
        {
            _bpm = Math.Clamp(value, 40, 200);
            if (State == CueEngineState.Running)
            {
                _pendingCues.Clear();
                _pendingGridLines.Clear();
                _pendingMelodic.Clear();
                _nextGridLineTime = DateTime.Now;
                _noteIndex = 0;
                _nextLoopOffset = 0.0;
                _songEnded = false;
                _melodicNoteIndex = 0;
                _melodicEventIndex = 0;
                _nextMelodicLoopOffset = 0.0;
                _melodicEnded = _currentSong.MelodicTrack is null;
            }
        }
    }

    public CueEngineState State         { get; private set; } = CueEngineState.Stopped;
    public DateTime        SongStartTime { get; private set; } = DateTime.MinValue;

    public event EventHandler? SongEnded;

    /// <summary>Fired whenever the playhead wraps via a loop boundary.</summary>
    public event EventHandler? LoopWrapped;

    public double EighthNoteMs => 60000.0 / (_bpm * 2.0);

    /// <summary>
    /// Current playhead position in song-absolute eighth-notes. -1 if not playing.
    /// When a loop is active, SongStartTime is loop-iteration anchored, so we
    /// fold the loop-relative elapsed time back into [loopStart, loopEnd).
    /// </summary>
    public double CurrentEighths
    {
        get
        {
            if (State == CueEngineState.Stopped || SongStartTime == DateTime.MinValue) return -1;
            DateTime now = State == CueEngineState.Paused ? _pausedAt : DateTime.Now;
            double rawElapsed = Math.Max(0, (now - SongStartTime).TotalMilliseconds / EighthNoteMs);

            if (_loop is { IsActive: true } loop)
            {
                double span = loop.EndEighths - loop.StartEighths;
                if (span <= 1e-9) return loop.StartEighths;
                double inLoop = rawElapsed - Math.Floor(rawElapsed / span) * span;
                return loop.StartEighths + inLoop;
            }

            return rawElapsed;
        }
    }

    public Song CurrentSong => _currentSong;

    private Song _currentSong = SongLibrary.BuiltIn[0];
    private int _noteIndex;
    private double _nextLoopOffset;
    private bool _songEnded;

    private int _melodicNoteIndex;
    private int _melodicEventIndex;
    private double _nextMelodicLoopOffset;
    private bool _melodicEnded = true;

    private readonly Queue<FallingCue> _pendingCues = new();
    private readonly Queue<DateTime> _pendingGridLines = new();
    private readonly PriorityQueue<MelodicCue, DateTime> _pendingMelodic = new();
    private DateTime _nextGridLineTime;
    private DateTime _pausedAt;

    public void LoadSong(Song song)
    {
        _currentSong = song;
        if (State == CueEngineState.Running) Restart();
    }

    public void Play()
    {
        switch (State)
        {
            case CueEngineState.Paused:
                var pauseDuration = DateTime.Now - _pausedAt;
                _nextGridLineTime += pauseDuration;
                SongStartTime     += pauseDuration;
                State = CueEngineState.Running;
                break;
            case CueEngineState.Stopped:
                Restart();
                if (_loop is { IsActive: true }) Seek(_loop.StartEighths);
                break;
        }
    }

    public void Pause()
    {
        if (State != CueEngineState.Running) return;
        _pausedAt = DateTime.Now;
        State = CueEngineState.Paused;
    }

    public void Restart()
    {
        var now = DateTime.Now;
        // Shift the song-start anchor forward by the intro duration so the
        // first iteration begins after a silent count-in. Subsequent loop
        // iterations don't get the intro because _nextLoopOffset advances
        // by TotalEighths only.
        var introOffset = TimeSpan.FromMilliseconds(_currentSong.IntroEighths * EighthNoteMs);
        _nextGridLineTime = now + introOffset;
        SongStartTime     = now + introOffset;
        _noteIndex        = 0;
        _nextLoopOffset   = 0.0;
        _songEnded        = false;
        _melodicNoteIndex = 0;
        _melodicEventIndex = 0;
        _nextMelodicLoopOffset = 0.0;
        _melodicEnded     = _currentSong.MelodicTrack is null;
        _pendingCues.Clear();
        _pendingGridLines.Clear();
        _pendingMelodic.Clear();
        State = CueEngineState.Running;
    }

    public void Stop()
    {
        State = CueEngineState.Stopped;
        _pendingCues.Clear();
        _pendingGridLines.Clear();
        _pendingMelodic.Clear();
    }

    public List<FallingCue> DrainCues(DateTime lookAheadUntil)
    {
        if (State == CueEngineState.Running) SeedCuesUpTo(lookAheadUntil);
        var result = new List<FallingCue>();
        while (_pendingCues.Count > 0 && _pendingCues.Peek().ScheduledHitTime <= lookAheadUntil)
            result.Add(_pendingCues.Dequeue());

        if (_songEnded && _melodicEnded && _pendingCues.Count == 0 && _pendingMelodic.Count == 0)
        {
            Stop();
            SongEnded?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public List<DateTime> DrainGridLines(DateTime lookAheadUntil)
    {
        if (State == CueEngineState.Running) SeedGridLinesUpTo(lookAheadUntil);
        var result = new List<DateTime>();
        while (_pendingGridLines.Count > 0 && _pendingGridLines.Peek() <= lookAheadUntil)
            result.Add(_pendingGridLines.Dequeue());
        return result;
    }

    public List<MelodicCue> DrainMelodicEvents(DateTime lookAheadUntil)
    {
        if (State == CueEngineState.Running) SeedMelodicEventsUpTo(lookAheadUntil);
        var result = new List<MelodicCue>();
        while (_pendingMelodic.TryPeek(out var cue, out var t) && t <= lookAheadUntil)
        {
            _pendingMelodic.Dequeue();
            result.Add(cue);
        }
        return result;
    }

    private void SeedCuesUpTo(DateTime until)
    {
        if (_songEnded || _currentSong.Notes.Length == 0) return;
        var notes = _currentSong.Notes;
        double eighthMs = EighthNoteMs;
        bool   loopActive = _loop is { IsActive: true };
        double loopEnd    = loopActive ? _loop!.EndEighths   : 0;
        double loopStart  = loopActive ? _loop!.StartEighths : 0;

        while (true)
        {
            // If looping a sub-section, wrap when the next note in this iteration
            // would land at-or-past the loop end. We compute the within-iteration
            // offset to compare cleanly, regardless of which iteration we're in.
            if (loopActive && _noteIndex < notes.Length)
            {
                double localOffset = notes[_noteIndex].OffsetInEighths;
                if (localOffset >= loopEnd - 1e-9)
                {
                    // Skip remainder, wrap to first note >= loopStart in next iteration.
                    _nextLoopOffset += (loopEnd - loopStart);
                    SeekNoteIndexTo(loopStart);
                    LoopWrapped?.Invoke(this, EventArgs.Empty);
                    // Bail if the loop range contains no drum notes; otherwise
                    // we'd wrap forever on the same out-of-range index.
                    if (_noteIndex >= notes.Length ||
                        notes[_noteIndex].OffsetInEighths >= loopEnd - 1e-9)
                        break;
                    continue;
                }
            }

            if (_noteIndex >= notes.Length)
            {
                if (loopActive)
                {
                    _nextLoopOffset += (loopEnd - loopStart);
                    SeekNoteIndexTo(loopStart);
                    LoopWrapped?.Invoke(this, EventArgs.Empty);
                    if (_noteIndex >= notes.Length ||
                        notes[_noteIndex].OffsetInEighths >= loopEnd - 1e-9)
                        break;
                    continue;
                }
                if (_currentSong.ShouldLoop)
                {
                    _noteIndex = 0;
                    _nextLoopOffset += _currentSong.TotalEighths;
                }
                else
                {
                    _songEnded = true;
                    break;
                }
            }

            // hitTime calc — for looped sub-section, subtract loopStart so the
            // first iteration's notes land at SongStartTime + (offset - loopStart).
            double absoluteOffset = loopActive
                ? _nextLoopOffset + notes[_noteIndex].OffsetInEighths - loopStart
                : _nextLoopOffset + notes[_noteIndex].OffsetInEighths;

            var hitTime = SongStartTime + TimeSpan.FromMilliseconds(absoluteOffset * eighthMs);

            if (hitTime > until) break;

            _pendingCues.Enqueue(new FallingCue(notes[_noteIndex].Lane, hitTime, notes[_noteIndex].Hand));
            _noteIndex++;
        }
    }

    /// <summary>
    /// Repositions <see cref="_noteIndex"/> to the first note whose offset is
    /// at or after <paramref name="targetEighths"/>. Uses linear scan (note
    /// counts are small in practice; binary search would be marginal).
    /// </summary>
    private void SeekNoteIndexTo(double targetEighths)
    {
        var notes = _currentSong.Notes;
        int lo = 0, hi = notes.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (notes[mid].OffsetInEighths < targetEighths - 1e-9) lo = mid + 1;
            else hi = mid;
        }
        _noteIndex = lo;
    }

    private void SeedGridLinesUpTo(DateTime until)
    {
        double eighthMs = EighthNoteMs;
        while (_nextGridLineTime <= until)
        {
            _pendingGridLines.Enqueue(_nextGridLineTime);
            _nextGridLineTime += TimeSpan.FromMilliseconds(eighthMs);
        }
    }

    // Walks Notes and ControlEvents in merged offset order, enqueuing NoteOn/NoteOff
    // pairs and control-event MIDI messages with absolute scheduled times. Uses a
    // priority queue because a NoteOff can land before the next NoteOn.
    //
    // When a loop is active, the same wrap math used for drum cues applies:
    // events past loopEnd in the current iteration are skipped, indices wrap to
    // the first event >= loopStart, and any note straddling loopEnd has its
    // NoteOff clamped to the boundary so it doesn't ring through the wrap.
    private void SeedMelodicEventsUpTo(DateTime until)
    {
        if (_melodicEnded || _currentSong.MelodicTrack is not { } track) return;

        double eighthMs = EighthNoteMs;
        var notes = track.Notes;
        var events = track.ControlEvents;

        bool   loopActive = _loop is { IsActive: true };
        double loopStart  = loopActive ? _loop!.StartEighths : 0;
        double loopEnd    = loopActive ? _loop!.EndEighths   : 0;

        while (true)
        {
            bool notesDone  = _melodicNoteIndex  >= notes.Length;
            bool eventsDone = _melodicEventIndex >= events.Length;

            // Wrap when looping: either next event is past loopEnd, or both
            // streams exhausted within this iteration.
            if (loopActive)
            {
                double nextLocal = double.MaxValue;
                if (!notesDone)  nextLocal = Math.Min(nextLocal, notes[_melodicNoteIndex].OffsetInEighths);
                if (!eventsDone) nextLocal = Math.Min(nextLocal, events[_melodicEventIndex].OffsetInEighths);

                if (nextLocal >= loopEnd - 1e-9)
                {
                    _nextMelodicLoopOffset += (loopEnd - loopStart);
                    SeekMelodicIndicesTo(loopStart);
                    // Bail if the loop range contains no melodic events; otherwise
                    // we'd wrap forever on the same out-of-range indices.
                    double afterWrap = double.MaxValue;
                    if (_melodicNoteIndex < notes.Length)
                        afterWrap = Math.Min(afterWrap, notes[_melodicNoteIndex].OffsetInEighths);
                    if (_melodicEventIndex < events.Length)
                        afterWrap = Math.Min(afterWrap, events[_melodicEventIndex].OffsetInEighths);
                    if (afterWrap >= loopEnd - 1e-9) break;
                    continue;
                }
            }
            else if (notesDone && eventsDone)
            {
                if (_currentSong.ShouldLoop)
                {
                    _melodicNoteIndex = 0;
                    _melodicEventIndex = 0;
                    _nextMelodicLoopOffset += _currentSong.TotalEighths;
                    continue;
                }
                _melodicEnded = true;
                break;
            }

            // Same time-anchor convention as drum cues: in loop mode,
            // SongStartTime is anchored to the start of the current loop
            // iteration, so subtract loopStart to get iteration-relative time.
            double iterShift = loopActive ? loopStart : 0;
            double nextNoteOffset = notesDone
                ? double.MaxValue
                : _nextMelodicLoopOffset + notes[_melodicNoteIndex].OffsetInEighths - iterShift;
            double nextEventOffset = eventsDone
                ? double.MaxValue
                : _nextMelodicLoopOffset + events[_melodicEventIndex].OffsetInEighths - iterShift;

            if (nextNoteOffset <= nextEventOffset)
            {
                var n = notes[_melodicNoteIndex];
                DateTime onTime = SongStartTime + TimeSpan.FromMilliseconds(nextNoteOffset * eighthMs);
                if (onTime > until) break;

                // If the note's tail would extend past loopEnd, clamp the
                // NoteOff to just before the boundary so the synth releases
                // cleanly before the next iteration's NoteOn arrives.
                double offEighths = nextNoteOffset + n.DurationInEighths;
                if (loopActive)
                {
                    double iterEnd = _nextMelodicLoopOffset + (loopEnd - loopStart);
                    if (offEighths > iterEnd) offEighths = iterEnd - 1e-3;
                }

                DateTime offTime = SongStartTime + TimeSpan.FromMilliseconds(offEighths * eighthMs);

                _pendingMelodic.Enqueue(
                    new MelodicCue(onTime, n.Channel, 0x90, (byte)n.NoteNumber, (byte)n.Velocity),
                    onTime);
                _pendingMelodic.Enqueue(
                    new MelodicCue(offTime, n.Channel, 0x80, (byte)n.NoteNumber, 0),
                    offTime);
                _melodicNoteIndex++;
            }
            else
            {
                DateTime evTime = SongStartTime + TimeSpan.FromMilliseconds(nextEventOffset * eighthMs);
                if (evTime > until) break;

                var ev = events[_melodicEventIndex];
                MelodicCue? cue = ev switch
                {
                    ProgramChange pc => new MelodicCue(evTime, pc.Channel, 0xC0, (byte)pc.Program, 0),
                    ControlChange cc => new MelodicCue(evTime, cc.Channel, 0xB0, (byte)cc.Controller, (byte)cc.Value),
                    PitchBend pb     => new MelodicCue(evTime, pb.Channel, 0xE0,
                                            (byte)(pb.Value & 0x7F),
                                            (byte)((pb.Value >> 7) & 0x7F)),
                    _ => null,
                };
                if (cue is not null)
                    _pendingMelodic.Enqueue(cue, evTime);
                _melodicEventIndex++;
            }
        }
    }

    // Mirror of SeekNoteIndexTo for the melodic track (notes + control events),
    // used on Seek and on loop wraps.
    private void SeekMelodicIndicesTo(double targetEighths)
    {
        if (_currentSong.MelodicTrack is not { } track)
        {
            _melodicNoteIndex = 0;
            _melodicEventIndex = 0;
            return;
        }

        int lo = 0, hi = track.Notes.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (track.Notes[mid].OffsetInEighths < targetEighths - 1e-9) lo = mid + 1;
            else hi = mid;
        }
        _melodicNoteIndex = lo;

        lo = 0; hi = track.ControlEvents.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (track.ControlEvents[mid].OffsetInEighths < targetEighths - 1e-9) lo = mid + 1;
            else hi = mid;
        }
        _melodicEventIndex = lo;
    }

    /// <summary>
    /// Repositions the playhead so that <paramref name="targetEighths"/> is the
    /// current song-relative position. Clears all pending queues and re-seeds
    /// the grid-line cursor.
    ///
    /// When a loop is active, SongStartTime is anchored so that "elapsed since
    /// SongStartTime" represents "eighths since the loop began this iteration"
    /// (i.e. <c>SongStartTime = now - (target - loopStart) * eighthMs</c>).
    /// When no loop is active it represents absolute song-eighths. This dual
    /// interpretation keeps the cue scheduling and grid math symmetric across
    /// both cases.
    /// </summary>
    public void Seek(double targetEighths)
    {
        double eighthMs = EighthNoteMs;
        DateTime now = DateTime.Now;
        targetEighths = Math.Max(0, targetEighths);

        bool   loopActive = _loop is { IsActive: true };
        double anchorEighths = loopActive
            ? targetEighths - _loop!.StartEighths
            : targetEighths;

        SongStartTime    = now - TimeSpan.FromMilliseconds(anchorEighths * eighthMs);
        _nextGridLineTime = now;
        _nextLoopOffset  = 0.0;
        SeekNoteIndexTo(targetEighths);
        SeekMelodicIndicesTo(targetEighths);
        _nextMelodicLoopOffset = 0.0;
        _melodicEnded     = _currentSong.MelodicTrack is null;
        _songEnded        = false;

        _pendingCues.Clear();
        _pendingGridLines.Clear();
        _pendingMelodic.Clear();
    }

    private void OnLoopChanged(object? sender, EventArgs e)
    {
        if (_loop is null) return;

        if (!_loop.IsActive)
        {
            // Loop cleared: keep playing in place; just flush stale schedule.
            if (State == CueEngineState.Running)
            {
                double pos = CurrentEighths;
                _pendingCues.Clear();
                _pendingGridLines.Clear();
                _pendingMelodic.Clear();
                _nextGridLineTime = DateTime.Now;
                SeekNoteIndexTo(pos);
                SeekMelodicIndicesTo(pos);
                _nextLoopOffset = 0.0;
                _nextMelodicLoopOffset = 0.0;
                _melodicEnded = _currentSong.MelodicTrack is null;
            }
            return;
        }

        // Loop active: if currently playing outside the new bounds, jump to start;
        // otherwise leave playhead alone but flush queues so the new end-bound
        // takes effect on the very next wrap (no need to re-seek mid-iteration
        // because the seed loop computes wraps relative to loopEnd dynamically).
        if (State == CueEngineState.Running)
        {
            double pos = CurrentEighths;
            if (pos < _loop.StartEighths - 1e-6 || pos >= _loop.EndEighths - 1e-6)
            {
                Seek(_loop.StartEighths);
            }
            else
            {
                _pendingCues.Clear();
                _pendingMelodic.Clear();
                // Walk both indices forward from current position within the loop
                // so the seed loops resume coherently from the same playhead.
                SeekNoteIndexTo(pos);
                SeekMelodicIndicesTo(pos);
            }
        }
    }
}
