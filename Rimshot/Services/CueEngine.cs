using System;
using System.Collections.Generic;
using Rimshot.Core.Models;

namespace Rimshot.Services;

public enum CueEngineState { Stopped, Running, Paused }

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
                _nextGridLineTime = DateTime.Now;
                _noteIndex = 0;
                _nextLoopOffset = 0.0;
                _songEnded = false;
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

    private readonly Queue<FallingCue> _pendingCues = new();
    private readonly Queue<DateTime> _pendingGridLines = new();
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
        _pendingCues.Clear();
        _pendingGridLines.Clear();
        State = CueEngineState.Running;
    }

    public void Stop()
    {
        State = CueEngineState.Stopped;
        _pendingCues.Clear();
        _pendingGridLines.Clear();
    }

    public List<FallingCue> DrainCues(DateTime lookAheadUntil)
    {
        if (State == CueEngineState.Running) SeedCuesUpTo(lookAheadUntil);
        var result = new List<FallingCue>();
        while (_pendingCues.Count > 0 && _pendingCues.Peek().ScheduledHitTime <= lookAheadUntil)
            result.Add(_pendingCues.Dequeue());

        if (_songEnded && _pendingCues.Count == 0)
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
}
