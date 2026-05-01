using System;
using System.Collections.Generic;
using Rimshot.Models;

namespace Rimshot.Services;

public enum CueEngineState { Stopped, Running, Paused }

public class CueEngine
{
    // Base demo pattern: (eighthNoteOffset within bar, laneIndex)
    // Lane order: HH=0, CR=1, SN=2, TM1=3, TM2=4, BD=5, FTM=6, RD=7
    private static readonly (int Offset, int Lane)[] _basePattern =
    [
        (0, 0), (0, 5),   // beat 1: HH + BD
        (1, 0),            // & 1:    HH
        (2, 0), (2, 2),   // beat 2: HH + SN
        (3, 0),            // & 2:    HH
        (4, 0), (4, 5),   // beat 3: HH + BD
        (5, 0),            // & 3:    HH
        (6, 0), (6, 2),   // beat 4: HH + SN
        (7, 0),            // & 4:    HH
    ];

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
                _nextBarTime = DateTime.Now;
                _nextGridLineTime = DateTime.Now;
                _barIndex = 0;
            }
        }
    }

    public CueEngineState State       { get; private set; } = CueEngineState.Stopped;
    public DateTime       SongStartTime { get; private set; } = DateTime.MinValue;

    private double EighthNoteMs => 60000.0 / (_bpm * 2.0);

    private readonly Queue<FallingCue> _pendingCues = new();
    private readonly Queue<DateTime> _pendingGridLines = new();
    private DateTime _nextBarTime;
    private DateTime _nextGridLineTime;
    private DateTime _pausedAt;
    private int _barIndex;

    public void Play()
    {
        switch (State)
        {
            case CueEngineState.Paused:
                var pauseDuration = DateTime.Now - _pausedAt;
                _nextBarTime      += pauseDuration;
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
        _nextBarTime      = now;
        _nextGridLineTime = now;
        SongStartTime     = now;
        _barIndex = 0;
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
        while (_nextBarTime <= until)
        {
            var barStart = _nextBarTime;
            double eighthMs = EighthNoteMs;

            foreach (var (offset, lane) in _basePattern)
                _pendingCues.Enqueue(new FallingCue(lane, barStart + TimeSpan.FromMilliseconds(offset * eighthMs)));

            // Crash on beat 1 every 4 bars (CR is lane 1)
            if (_barIndex % 4 == 0)
                _pendingCues.Enqueue(new FallingCue(1, barStart));

            _nextBarTime = barStart + TimeSpan.FromMilliseconds(8 * eighthMs);
            _barIndex++;
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
