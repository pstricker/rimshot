using System;
using System.Collections.Generic;

namespace DrumApp.Services;

public enum MetronomeSubdivision { Whole = 1, Half = 2, Quarter = 4, Eighth = 8 }

public class MetronomeService
{
    private int _bpm = 90;
    public int Bpm
    {
        get => _bpm;
        set
        {
            _bpm = Math.Clamp(value, 40, 200);
            if (_running) ResetTiming();
        }
    }

    private MetronomeSubdivision _subdivision = MetronomeSubdivision.Quarter;
    public MetronomeSubdivision Subdivision
    {
        get => _subdivision;
        set
        {
            _subdivision = value;
            if (_running) ResetTiming();
        }
    }

    public bool IsEnabled { get; set; } = false;

    private double TickIntervalMs => 240000.0 / _bpm / (int)_subdivision;

    private DateTime _startTime;
    private DateTime _nextTickTime;
    private DateTime _pausedAt;
    private bool _running;
    private readonly Queue<DateTime> _pendingTicks = new();

    public void Start()
    {
        _pendingTicks.Clear();
        _startTime = DateTime.Now;
        _nextTickTime = _startTime;
        _running = true;
    }

    public void Pause()
    {
        if (!_running) return;
        _pausedAt = DateTime.Now;
        _running = false;
    }

    public void Resume()
    {
        if (_running) return;
        var pauseDuration = DateTime.Now - _pausedAt;
        _nextTickTime += pauseDuration;
        _startTime += pauseDuration;
        _running = true;
    }

    public void Stop()
    {
        _running = false;
        _pendingTicks.Clear();
    }

    public List<DateTime> DrainTicks(DateTime lookAheadUntil)
    {
        if (_running && IsEnabled) SeedTicksUpTo(lookAheadUntil);
        var result = new List<DateTime>();
        while (_pendingTicks.Count > 0 && _pendingTicks.Peek() <= lookAheadUntil)
            result.Add(_pendingTicks.Dequeue());
        return result;
    }

    public bool IsOnBeat(DateTime hitTime, double toleranceMs = 75.0)
    {
        if (!_running) return false;
        double elapsed = (hitTime - _startTime).TotalMilliseconds;
        double interval = TickIntervalMs;
        double phase = ((elapsed % interval) + interval) % interval;
        return phase <= toleranceMs || phase >= interval - toleranceMs;
    }

    private void SeedTicksUpTo(DateTime until)
    {
        while (_nextTickTime <= until)
        {
            _pendingTicks.Enqueue(_nextTickTime);
            _nextTickTime += TimeSpan.FromMilliseconds(TickIntervalMs);
        }
    }

    private void ResetTiming()
    {
        _pendingTicks.Clear();
        _startTime = DateTime.Now;
        _nextTickTime = _startTime;
    }
}
