using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Rimshot.Core.Models;
using Rimshot.Services;

namespace Rimshot.Views;

public partial class CueView : UserControl
{
    private const double TravelMs = 2000.0;
    private const double HitZoneFraction = 0.85;
    private const double HitWindowMs = 150.0;
    private const double RingDurationMs = 350.0;

    private static readonly FontFamily s_bebasNeue = new("avares://Rimshot.Core/Assets/Fonts#Bebas Neue");

    // Kit space (0–7): all pads always visible
    private static readonly IReadOnlyList<DrumLane> _allLanes = DrumLane.StandardKit();
    private readonly SolidColorBrush[] _laneBrushes;
    private DispatcherTimer? _timer;

    // Active song lanes — subset of _allLanes (kit order preserved)
    private IReadOnlyList<DrumLane> _activeLanes = DrumLane.StandardKit();
    // Maps kit index (0–7) → active track position, or -1 if not active
    private readonly int[] _kitToActive = new int[8];

    // Kit layout — bottom 2D reference (symmetric around BD at X=0.50)
    private static readonly (double Xf, double Yf, double Df)[] _padLayout =
    [
        (0.21, 0.78, 0.130), // HH  — far left
        (0.33, 0.68, 0.125), // CR  — upper left
        (0.31, 0.86, 0.130), // SN  — lower left
        (0.44, 0.74, 0.110), // TM1 — upper center-left
        (0.56, 0.74, 0.110), // TM2 — upper center-right
        (0.50, 0.88, 0.120), // BD  — center
        (0.69, 0.86, 0.130), // FTM — lower right
        (0.67, 0.68, 0.125), // RD  — upper right
    ];
    private readonly double[] _padCX     = new double[8];
    private readonly double[] _padCY     = new double[8];
    private readonly double[] _padDiamPx = new double[8];

    // Horizontal lane track positions — variable-size, active-position indexed
    private double    _canvasW;
    private double    _laneSpacing;
    private double    _hitZoneX;
    private double[]     _laneY          = new double[8];
    private Ellipse?[]   _laneIndicators  = new Ellipse?[8];
    private Ellipse?[]   _indInnerRings   = new Ellipse?[8];
    private TextBlock?[] _laneIndLabels   = new TextBlock?[8];
    private Line?[]      _laneTrackLines  = new Line?[8];

    // Kit pad elements — always 8, kit-indexed
    private readonly Ellipse?[]   _pads          = new Ellipse?[8];
    private readonly Ellipse?[]   _padInnerRings = new Ellipse?[8];
    private readonly TextBlock?[] _padLabels     = new TextBlock?[8];

    private static bool IsCymbal(int lane) => lane is 0 or 1 or 7;
    private static bool IsDrum(int lane)   => lane is 2 or 3 or 4 or 6;

    // Beat grid: hit-zone line + scrolling subdivision lines
    private Line? _hitZoneLine;
    private const int GridLinePoolSize = 60;
    private readonly Line[] _gridLinePool = new Line[GridLinePoolSize];
    private bool _gridPoolReady;

    // Flash state — kit-indexed
    private readonly DateTime[] _padFlashUntil = new DateTime[8];
    private readonly bool[]     _isFlashing    = new bool[8];

    // Active scrolling note cues
    private sealed class ActiveCue
    {
        public required Rectangle Visual;
        public DateTime HitTime;
        public int Lane;
        public bool AutoFired;
    }
    private readonly List<ActiveCue> _activeCues = [];

    // On-target hit rings
    private readonly List<(Ellipse Visual, DateTime StartTime, int Lane)> _hitRings = [];

    // HH open/closed indicator
    private TextBlock? _hhStateLabel;

    private double CueWidth  => Math.Clamp(_laneSpacing * 0.35, 6.0, 12.0);
    private double CueHeight => Math.Clamp(_laneSpacing * 0.65, 16.0, 40.0);

    public MidiService? Midi { get; set; }
    public CueEngine? Engine { get; set; }
    public MetronomeService? Metronome { get; set; }
    public bool AutoPlay { get; set; }
    public AudioService? Audio { get; set; }

    public void SetHiHatOpen(bool isOpen)
    {
        if (_hhStateLabel != null)
            _hhStateLabel.Text = isOpen ? "○" : "●";
    }

    private void OnMetronomeToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Metronome != null)
            Metronome.IsEnabled = MetronomeToggle.IsChecked == true;
    }

    public CueView()
    {
        InitializeComponent();
        _laneBrushes = new SolidColorBrush[8];
        for (int i = 0; i < 8; i++)
            _laneBrushes[i] = new SolidColorBrush(_allLanes[i].Color);

        // Identity map: all 8 lanes active by default
        for (int i = 0; i < 8; i++) _kitToActive[i] = i;
    }

    public void SetActiveLanes(IReadOnlyList<DrumLane> lanes)
    {
        ClearCues();
        RemoveTrackVisuals();

        for (int i = 0; i < 8; i++) _kitToActive[i] = -1;
        for (int j = 0; j < lanes.Count; j++) _kitToActive[lanes[j].Index] = j;
        _activeLanes = lanes;

        int n = lanes.Count;
        _laneY          = new double[n];
        _laneIndicators  = new Ellipse?[n];
        _indInnerRings   = new Ellipse?[n];
        _laneIndLabels   = new TextBlock?[n];
        _laneTrackLines  = new Line?[n];

        RebuildLayout();
    }

    private void RemoveTrackVisuals()
    {
        var canvas = CueCanvas;
        if (canvas == null) return;
        for (int i = 0; i < _laneIndicators.Length; i++)
        {
            if (_laneIndicators[i]  is { } ind) canvas.Children.Remove(ind);
            if (_indInnerRings[i]   is { } ir)  canvas.Children.Remove(ir);
            if (_laneIndLabels[i]   is { } lbl) canvas.Children.Remove(lbl);
            if (_laneTrackLines[i]  is { } tl)  canvas.Children.Remove(tl);
        }
        if (_hhStateLabel != null) { canvas.Children.Remove(_hhStateLabel); _hhStateLabel = null; }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Midi != null) Midi.DrumHitReceived += OnDrumHit;
        SizeChanged += OnSizeChanged;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        _timer = null;
        SizeChanged -= OnSizeChanged;
        if (Midi != null) Midi.DrumHitReceived -= OnDrumHit;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => RebuildLayout();

    private void RebuildLayout()
    {
        var canvas = CueCanvas;
        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        _canvasW = w;

        // Pad positions — always 8, kit-indexed
        for (int i = 0; i < 8; i++)
        {
            _padCX[i]     = _padLayout[i].Xf * w;
            _padCY[i]     = _padLayout[i].Yf * h;
            _padDiamPx[i] = _padLayout[i].Df * h;
        }

        _hitZoneX = _padCX[5]; // centered above BD

        // Track positions — active-lane count only.
        // Two-pass layout: first pass computes laneSpacing to get indDiam, second pass
        // adjusts trackTop so the top indicator circle doesn't clip off-screen.
        int n = _activeLanes.Count;
        double trackBottom = h * 0.60;
        double trackTop    = h * 0.05;
        _laneSpacing = n > 1 ? (trackBottom - trackTop) / (n - 1) : (trackBottom - trackTop);
        double indDiam = Math.Clamp(_laneSpacing * 0.60, 14, w * 0.10);
        trackTop  = Math.Max(trackTop, indDiam / 2 + 6);
        _laneSpacing = n > 1 ? (trackBottom - trackTop) / (n - 1) : (trackBottom - trackTop);
        indDiam = Math.Clamp(_laneSpacing * 0.60, 14, w * 0.10); // recompute after trackTop adjustment

        for (int j = 0; j < n; j++)
            _laneY[j] = n > 1 ? trackTop + _laneSpacing * j : (trackTop + trackBottom) / 2;

        double lineTop    = _laneY[0]     - _laneSpacing / 2;
        double lineBottom = _laneY[n - 1] + _laneSpacing / 2;

        // Hit-zone vertical line
        if (_hitZoneLine == null)
        {
            _hitZoneLine = new Line
            {
                Stroke          = new SolidColorBrush(Colors.White),
                StrokeThickness = 2,
                ZIndex          = -1,
                Opacity         = 0.70,
            };
            canvas.Children.Add(_hitZoneLine);
        }
        _hitZoneLine.StartPoint = new Point(_hitZoneX, lineTop);
        _hitZoneLine.EndPoint   = new Point(_hitZoneX, lineBottom);

        // Subdivision grid line pool
        if (!_gridPoolReady)
        {
            for (int k = 0; k < GridLinePoolSize; k++)
            {
                var gl = new Line
                {
                    Stroke          = new SolidColorBrush(Colors.White),
                    StrokeThickness = 1,
                    ZIndex          = -2,
                    Opacity         = 0,
                };
                _gridLinePool[k] = gl;
                canvas.Children.Add(gl);
            }
            _gridPoolReady = true;
        }
        for (int k = 0; k < GridLinePoolSize; k++)
        {
            _gridLinePool[k].StartPoint = new Point(_gridLinePool[k].StartPoint.X, lineTop);
            _gridLinePool[k].EndPoint   = new Point(_gridLinePool[k].EndPoint.X,   lineBottom);
        }

        double fontSize = Math.Clamp(indDiam * 0.45, 10, 18);
        double indCX    = 6 + indDiam / 2;

        // Horizontal lane track guide lines — active-position indexed
        for (int j = 0; j < n; j++)
        {
            int kitIdx = _activeLanes[j].Index;
            if (_laneTrackLines[j] == null)
            {
                var tl = new Line { StrokeThickness = 1, ZIndex = -3, Opacity = 0.18 };
                _laneTrackLines[j] = tl;
                canvas.Children.Add(tl);
            }
            var trackLine = _laneTrackLines[j]!;
            trackLine.Stroke     = _laneBrushes[kitIdx];
            trackLine.StartPoint = new Point(0, _laneY[j]);
            trackLine.EndPoint   = new Point(w, _laneY[j]);
        }

        // Left-side lane indicators — active-position indexed
        for (int j = 0; j < n; j++)
        {
            int kitIdx = _activeLanes[j].Index;
            if (_laneIndicators[j] == null)
            {
                var ind = new Ellipse { ZIndex = 1 };
                var lbl = new TextBlock
                {
                    Foreground    = Brushes.White,
                    FontFamily    = s_bebasNeue,
                    TextAlignment = TextAlignment.Center,
                    ZIndex        = 2,
                };
                _laneIndicators[j] = ind;
                _laneIndLabels[j]  = lbl;
                canvas.Children.Add(ind);
                canvas.Children.Add(lbl);
            }

            var indicator = _laneIndicators[j]!;
            indicator.Width           = indDiam;
            indicator.Height          = indDiam;
            indicator.Fill            = _isFlashing[kitIdx] ? Brushes.White : _laneBrushes[kitIdx];
            indicator.Stroke          = IsDrum(kitIdx) ? Brushes.White : null;
            indicator.StrokeThickness = IsDrum(kitIdx) ? Math.Max(1, indDiam * 0.07) : 0;
            Canvas.SetLeft(indicator, indCX - indDiam / 2);
            Canvas.SetTop(indicator,  _laneY[j] - indDiam / 2);

            // Cymbal inner black ring
            if (IsCymbal(kitIdx))
            {
                if (_indInnerRings[j] == null)
                {
                    var ring = new Ellipse { Fill = Brushes.Transparent, Stroke = Brushes.Black, ZIndex = 2 };
                    _indInnerRings[j] = ring;
                    canvas.Children.Add(ring);
                }
                double rDiam = indDiam * 0.82;
                var ir = _indInnerRings[j]!;
                ir.Width = rDiam; ir.Height = rDiam;
                ir.StrokeThickness = Math.Max(0.5, indDiam * 0.030);
                Canvas.SetLeft(ir, indCX - rDiam / 2);
                Canvas.SetTop(ir,  _laneY[j] - rDiam / 2);
            }

            var label = _laneIndLabels[j]!;
            label.Text     = _activeLanes[j].Label;
            label.FontSize = fontSize;
            label.Width    = indDiam;
            Canvas.SetLeft(label, indCX - indDiam / 2);
            Canvas.SetTop(label,  _laneY[j] - fontSize / 2 - 1);
        }

        // HH open/closed state label — only if HH is active
        int hhActivePos = _kitToActive[0];
        if (hhActivePos >= 0)
        {
            if (_hhStateLabel == null)
            {
                _hhStateLabel = new TextBlock { Text = "●", Foreground = _laneBrushes[0] };
                canvas.Children.Add(_hhStateLabel);
            }
            _hhStateLabel.FontSize = fontSize;
            Canvas.SetLeft(_hhStateLabel, indCX + indDiam / 2 + 3);
            Canvas.SetTop(_hhStateLabel,  _laneY[hhActivePos] - fontSize / 2 - 1);
        }

        // Kit pads at bottom — always 8, kit-indexed, UNCHANGED
        for (int i = 0; i < 8; i++)
        {
            if (_pads[i] == null)
            {
                var pad = new Ellipse { ZIndex = 0 };
                var lbl = new TextBlock { Foreground = Brushes.White, TextAlignment = TextAlignment.Center, ZIndex = 3, FontFamily = s_bebasNeue };
                _pads[i]      = pad;
                _padLabels[i] = lbl;
                canvas.Children.Add(pad);
                canvas.Children.Add(lbl);
            }

            double diam = _padDiamPx[i];
            var p = _pads[i]!;
            p.Width           = diam;
            p.Height          = diam;
            p.Fill            = _isFlashing[i] ? Brushes.White : _laneBrushes[i];
            p.Stroke          = IsDrum(i) ? Brushes.White : null;
            p.StrokeThickness = IsDrum(i) ? Math.Max(2, diam * 0.05) : 0;
            Canvas.SetLeft(p, _padCX[i] - diam / 2.0);
            Canvas.SetTop(p,  _padCY[i] - diam / 2.0);

            if (IsCymbal(i))
            {
                if (_padInnerRings[i] == null)
                {
                    var ring = new Ellipse { Fill = Brushes.Transparent, Stroke = Brushes.Black, ZIndex = 2 };
                    _padInnerRings[i] = ring;
                    canvas.Children.Add(ring);
                }
                double rDiam = diam * 0.84;
                var ir = _padInnerRings[i]!;
                ir.Width = rDiam; ir.Height = rDiam;
                ir.StrokeThickness = Math.Max(1, diam * 0.028);
                Canvas.SetLeft(ir, _padCX[i] - rDiam / 2);
                Canvas.SetTop(ir,  _padCY[i] - rDiam / 2);
            }

            double padFontSize = Math.Clamp(diam * 0.22, 10, 16);
            var l = _padLabels[i]!;
            l.Text     = _allLanes[i].Label;
            l.FontSize = padFontSize;
            l.Width    = diam;
            Canvas.SetLeft(l, _padCX[i] - diam / 2);
            Canvas.SetTop(l,  _padCY[i] - padFontSize * 0.65);
        }

        var now = DateTime.Now;
        foreach (var cue in _activeCues)
        {
            var (cx, cy) = CuePosition(cue.HitTime, now, cue.Lane);
            double cW = CueWidth;
            double cH = CueHeight;
            Canvas.SetLeft(cue.Visual, cx - cW / 2);
            Canvas.SetTop(cue.Visual,  cy - cH / 2);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var canvas = CueCanvas;
        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;

        if ((_pads[0] == null || _laneY.Length != _activeLanes.Count) && w > 0 && h > 0)
            RebuildLayout();

        if (w <= 0 || h <= 0) return;

        var now       = DateTime.Now;
        var lookAhead = now + TimeSpan.FromMilliseconds(TravelMs);

        if (Engine != null)
        {
            foreach (var cue in Engine.DrainCues(lookAhead))
            {
                // Skip cues for lanes not in the active set
                int activePos = cue.Lane < 8 ? _kitToActive[cue.Lane] : -1;
                if (activePos < 0) continue;

                double cW = CueWidth;
                double cH = CueHeight;
                var rect = new Rectangle
                {
                    Width   = cW,
                    Height  = cH,
                    Fill    = _laneBrushes[cue.Lane],
                    RadiusX = 3,
                    RadiusY = 3,
                };
                var (cx, cy) = CuePosition(cue.ScheduledHitTime, now, cue.Lane);
                Canvas.SetLeft(rect, cx - cW / 2);
                Canvas.SetTop(rect,  cy - cH / 2);
                canvas.Children.Add(rect);
                _activeCues.Add(new ActiveCue
                {
                    Visual  = rect,
                    HitTime = cue.ScheduledHitTime,
                    Lane    = cue.Lane,
                });
            }
            Engine.DrainGridLines(lookAhead);
        }

        // Metronome audio
        if (Metronome?.IsEnabled == true && Audio != null)
        {
            foreach (var _ in Metronome.DrainTicks(now))
                Audio.PlayMetronomeClick();
        }

        // Update and expire note cues; auto-fire when AutoPlay is on
        for (int i = _activeCues.Count - 1; i >= 0; i--)
        {
            var cue = _activeCues[i];
            var (cx, cy) = CuePosition(cue.HitTime, now, cue.Lane);
            if (cx > _hitZoneX + _laneSpacing * 0.5)
            {
                canvas.Children.Remove(cue.Visual);
                _activeCues.RemoveAt(i);
            }
            else
            {
                if (AutoPlay && !cue.AutoFired && now >= cue.HitTime)
                {
                    AutoFireCue(cue, now);
                    cue.AutoFired = true;
                }

                double cW = CueWidth;
                double cH = CueHeight;
                Canvas.SetLeft(cue.Visual, cx - cW / 2);
                Canvas.SetTop(cue.Visual,  cy - cH / 2);
            }
        }

        // Pad and indicator flash — kit-indexed for pads, mapped to active pos for indicators
        for (int kitIdx = 0; kitIdx < 8; kitIdx++)
        {
            bool shouldFlash = now < _padFlashUntil[kitIdx];
            if (shouldFlash != _isFlashing[kitIdx])
            {
                _isFlashing[kitIdx] = shouldFlash;
                IBrush fill = shouldFlash ? Brushes.White : _laneBrushes[kitIdx];
                if (_pads[kitIdx] != null) _pads[kitIdx]!.Fill = fill;
                int ap = _kitToActive[kitIdx];
                if (ap >= 0 && _laneIndicators[ap] != null) _laneIndicators[ap]!.Fill = fill;
            }
        }

        // Hit rings — expand and fade
        for (int i = _hitRings.Count - 1; i >= 0; i--)
        {
            var (ring, startTime, lane) = _hitRings[i];
            double progress = (now - startTime).TotalMilliseconds / RingDurationMs;
            if (progress >= 1.0)
            {
                canvas.Children.Remove(ring);
                _hitRings.RemoveAt(i);
                continue;
            }
            double size = _padDiamPx[lane] * (1.0 + progress * 1.4);
            ring.Width   = size;
            ring.Height  = size;
            ring.Opacity = 1.0 - progress;
            Canvas.SetLeft(ring, _padCX[lane] - size / 2.0);
            Canvas.SetTop(ring,  _padCY[lane] - size / 2.0);
        }

        UpdateGridLines(now);
    }

    private void UpdateGridLines(DateTime now)
    {
        if (!_gridPoolReady || _laneSpacing <= 0) return;

        int poolIdx = 0;

        if (Engine?.State == CueEngineState.Running && Engine.SongStartTime != DateTime.MinValue)
        {
            double eighthMs  = 30000.0 / Engine.Bpm;
            double elapsed   = (now - Engine.SongStartTime).TotalMilliseconds;
            double lineTop   = _laneY[0]                    - _laneSpacing / 2;
            double lineBot   = _laneY[_activeLanes.Count - 1] + _laneSpacing / 2;

            int firstBeat = Math.Max(0, (int)((elapsed - TravelMs * 0.25) / eighthMs));
            int lastBeat  = (int)((elapsed + TravelMs)  / eighthMs) + 1;

            for (int beat = firstBeat; beat <= lastBeat && poolIdx < GridLinePoolSize; beat++)
            {
                double opacity = (beat % 8 == 0) ? 0.0
                               : (beat % 4 == 0) ? 0.5
                               : (beat % 2 == 0) ? 0.25
                               :                   0.125;

                if (opacity == 0) continue;

                DateTime beatTime  = Engine.SongStartTime.AddMilliseconds(beat * eighthMs);
                double   remaining = (beatTime - now).TotalMilliseconds;
                double   progress  = 1.0 - remaining / TravelMs;
                double   x         = -20 + progress * (_hitZoneX + 20);

                if (x < -2 || x > _canvasW + 2) continue;

                var line = _gridLinePool[poolIdx++];
                line.StartPoint = new Point(x, lineTop);
                line.EndPoint   = new Point(x, lineBot);
                line.Opacity    = opacity;
            }
        }

        for (int k = poolIdx; k < GridLinePoolSize; k++)
            _gridLinePool[k].Opacity = 0;
    }

    private (double X, double Y) CuePosition(DateTime hitTime, DateTime now, int kitLane)
    {
        double remainingMs = (hitTime - now).TotalMilliseconds;
        double progress    = 1.0 - remainingMs / TravelMs;
        double x           = -20 + progress * (_hitZoneX + 20);
        int    activePos   = kitLane < 8 ? _kitToActive[kitLane] : -1;
        double y           = activePos >= 0 ? _laneY[activePos] : _laneY[0];
        return (x, y);
    }

    private void OnDrumHit(object? sender, DrumHit hit)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var now = DateTime.Now;
            for (int i = 0; i < 8; i++)
            {
                if (Array.IndexOf(_allLanes[i].NoteNumbers, hit.NoteNumber) < 0) continue;

                _padFlashUntil[i] = now.AddMilliseconds(150);

                bool onBeat = Metronome?.IsEnabled == true && Metronome.IsOnBeat(now);

                for (int j = 0; j < _activeCues.Count; j++)
                {
                    var cue = _activeCues[j];
                    if (cue.Lane == i && Math.Abs((cue.HitTime - now).TotalMilliseconds) <= HitWindowMs)
                    {
                        SpawnHitRing(i, now, onBeat);
                        break;
                    }
                }
                break;
            }
        });
    }

    private void AutoFireCue(ActiveCue cue, DateTime now)
    {
        int lane = cue.Lane;
        int noteNumber = _allLanes[lane].NoteNumbers[0];
        Audio?.Play(lane, noteNumber, 1.0f);
        _padFlashUntil[lane] = now.AddMilliseconds(150);
        SpawnHitRing(lane, now);
    }

    private void SpawnHitRing(int lane, DateTime now, bool onBeat = false)
    {
        var ring = new Ellipse
        {
            Fill            = Brushes.Transparent,
            Stroke          = onBeat ? Brushes.LimeGreen : _laneBrushes[lane],
            StrokeThickness = onBeat ? 4 : 3,
            Width           = _padDiamPx[lane],
            Height          = _padDiamPx[lane],
        };
        Canvas.SetLeft(ring, _padCX[lane] - _padDiamPx[lane] / 2);
        Canvas.SetTop(ring,  _padCY[lane] - _padDiamPx[lane] / 2);
        CueCanvas.Children.Add(ring);
        _hitRings.Add((ring, now, lane));
    }

    public void ClearCues()
    {
        foreach (var cue in _activeCues)
            CueCanvas.Children.Remove(cue.Visual);
        _activeCues.Clear();

        foreach (var (ring, _, _) in _hitRings)
            CueCanvas.Children.Remove(ring);
        _hitRings.Clear();
    }
}
