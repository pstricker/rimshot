using System;
using System.Collections.Generic;
using Rimshot.Core.Models;

namespace Rimshot.Services;

public enum CueEngineState { Stopped, Running, Paused }

public record MelodicCue(DateTime ScheduledAt, int Channel, byte Command, byte Data1, byte Data2);

public class CueEngine
{
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

    private double EighthNoteMs => 60000.0 / (_bpm * 2.0);

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
        _nextGridLineTime = now;
        SongStartTime     = now;
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

        while (true)
        {
            if (_noteIndex >= notes.Length)
            {
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

            var hitTime = SongStartTime + TimeSpan.FromMilliseconds(
                (_nextLoopOffset + notes[_noteIndex].OffsetInEighths) * eighthMs);

            if (hitTime > until) break;

            _pendingCues.Enqueue(new FallingCue(notes[_noteIndex].Lane, hitTime));
            _noteIndex++;
        }
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
    private void SeedMelodicEventsUpTo(DateTime until)
    {
        if (_melodicEnded || _currentSong.MelodicTrack is not { } track) return;

        double eighthMs = EighthNoteMs;
        var notes = track.Notes;
        var events = track.ControlEvents;

        while (true)
        {
            bool notesDone  = _melodicNoteIndex  >= notes.Length;
            bool eventsDone = _melodicEventIndex >= events.Length;

            if (notesDone && eventsDone)
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

            double nextNoteOffset = notesDone
                ? double.MaxValue
                : _nextMelodicLoopOffset + notes[_melodicNoteIndex].OffsetInEighths;
            double nextEventOffset = eventsDone
                ? double.MaxValue
                : _nextMelodicLoopOffset + events[_melodicEventIndex].OffsetInEighths;

            if (nextNoteOffset <= nextEventOffset)
            {
                var n = notes[_melodicNoteIndex];
                DateTime onTime = SongStartTime + TimeSpan.FromMilliseconds(nextNoteOffset * eighthMs);
                if (onTime > until) break;

                DateTime offTime = SongStartTime + TimeSpan.FromMilliseconds(
                    (nextNoteOffset + n.DurationInEighths) * eighthMs);

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
}
