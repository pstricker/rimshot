using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using DrumApp.Models;
using DrumApp.Services;

namespace DrumApp.Views;

public partial class CueView : UserControl
{
    private const double TravelMs = 2000.0;
    private const double HitZoneFraction = 0.85;
    private const int LaneCount = 8;
    private const double HitWindowMs = 150.0;
    private const double RingDurationMs = 350.0;

    private readonly IReadOnlyList<DrumLane> _lanes = DrumLane.StandardKit();
    private readonly SolidColorBrush[] _laneBrushes;
    private DispatcherTimer? _timer;

    // Kit layout — bottom 2D reference (symmetric around BD at X=0.50)
    // (Xf = fraction of canvas width, Yf = fraction of canvas height, Df = fraction of canvas height)
    private static readonly (double Xf, double Yf, double Df)[] _padLayout =
    [
        (0.21, 0.78, 0.130), // HH  — far left
        (0.33, 0.68, 0.125), // CR  — upper left  (BD-0.17)
        (0.31, 0.86, 0.130), // SN  — lower left  (BD-0.19)
        (0.44, 0.74, 0.110), // TM1 — upper center-left
        (0.56, 0.74, 0.110), // TM2 — upper center-right
        (0.50, 0.88, 0.120), // BD  — center
        (0.69, 0.86, 0.130), // FTM — lower right (BD+0.19, mirrors SN)
        (0.67, 0.68, 0.125), // RD  — upper right (BD+0.17, mirrors CR)
    ];
    private readonly double[] _padCX     = new double[LaneCount];
    private readonly double[] _padCY     = new double[LaneCount];
    private readonly double[] _padDiamPx = new double[LaneCount];

    // Horizontal lane track positions
    private double   _canvasW;
    private double   _laneSpacing;
    private double   _hitZoneX;
    private readonly double[] _laneY = new double[LaneCount];

    // Fixed canvas elements — left-side indicators and kit pads
    private readonly Ellipse?[]   _laneIndicators  = new Ellipse?[LaneCount];
    private readonly Ellipse?[]   _indInnerRings   = new Ellipse?[LaneCount]; // cymbal inner ring on indicator
    private readonly TextBlock?[] _laneIndLabels   = new TextBlock?[LaneCount];
    private readonly Ellipse?[]   _pads            = new Ellipse?[LaneCount];
    private readonly Ellipse?[]   _padInnerRings   = new Ellipse?[LaneCount]; // cymbal inner ring on pad
    private readonly TextBlock?[] _padLabels       = new TextBlock?[LaneCount];

    private static bool IsCymbal(int lane) => lane is 0 or 1 or 7;
    private static bool IsDrum(int lane)   => lane is 2 or 3 or 4 or 6;

    // Beat grid: hit-zone line + scrolling subdivision lines
    private Line? _hitZoneLine;
    private const int GridLinePoolSize = 60;
    private readonly Line[] _gridLinePool = new Line[GridLinePoolSize];
    private bool _gridPoolReady;

    // Flash state
    private readonly DateTime[] _padFlashUntil = new DateTime[LaneCount];
    private readonly bool[] _isFlashing = new bool[LaneCount];

    // Active scrolling note cues
    private readonly List<(Rectangle Visual, DateTime HitTime, int Lane)> _activeCues = [];

    // On-target hit rings
    private readonly List<(Ellipse Visual, DateTime StartTime, int Lane)> _hitRings = [];

    // HH open/closed indicator
    private TextBlock? _hhStateLabel;

    public MidiService? Midi { get; set; }
    public CueEngine? Engine { get; set; }
    public MetronomeService? Metronome { get; set; }
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
        _laneBrushes = new SolidColorBrush[LaneCount];
        for (int i = 0; i < LaneCount; i++)
            _laneBrushes[i] = new SolidColorBrush(_lanes[i].Color);
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

        for (int i = 0; i < LaneCount; i++)
        {
            _padCX[i]     = _padLayout[i].Xf * w;
            _padCY[i]     = _padLayout[i].Yf * h;
            _padDiamPx[i] = _padLayout[i].Df * h;
        }

        _hitZoneX = _padCX[5]; // centered above BD

        // Evenly-spaced horizontal tracks in the upper portion
        double trackTop    = h * 0.03;
        double trackBottom = h * 0.60;
        _laneSpacing = (trackBottom - trackTop) / (LaneCount - 1);
        for (int i = 0; i < LaneCount; i++)
            _laneY[i] = trackTop + _laneSpacing * i;

        double lineTop    = _laneY[0]            - _laneSpacing / 2;
        double lineBottom = _laneY[LaneCount - 1] + _laneSpacing / 2;

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
        // Update heights for all pool lines
        for (int k = 0; k < GridLinePoolSize; k++)
        {
            _gridLinePool[k].StartPoint = new Point(_gridLinePool[k].StartPoint.X, lineTop);
            _gridLinePool[k].EndPoint   = new Point(_gridLinePool[k].EndPoint.X,   lineBottom);
        }

        double fontSize = Math.Clamp(h * 0.020, 10, 13);
        double indDiam  = Math.Clamp(_laneSpacing * 0.60, 14, 32);
        double indCX    = 6 + indDiam / 2;
        double lblX     = 6 + indDiam + 6;

        // Left-side lane indicators
        for (int i = 0; i < LaneCount; i++)
        {
            if (_laneIndicators[i] == null)
            {
                var ind = new Ellipse { ZIndex = 1 };
                var lbl = new TextBlock { Foreground = Brushes.White };
                _laneIndicators[i] = ind;
                _laneIndLabels[i]  = lbl;
                canvas.Children.Add(ind);
                canvas.Children.Add(lbl);
            }

            var indicator = _laneIndicators[i]!;
            indicator.Width          = indDiam;
            indicator.Height         = indDiam;
            indicator.Fill           = _isFlashing[i] ? Brushes.White : _laneBrushes[i];
            indicator.Stroke          = IsDrum(i) ? Brushes.White : null;
            indicator.StrokeThickness = IsDrum(i) ? Math.Max(1, indDiam * 0.07) : 0;
            Canvas.SetLeft(indicator, indCX - indDiam / 2);
            Canvas.SetTop(indicator,  _laneY[i] - indDiam / 2);

            // Cymbal inner black ring
            if (IsCymbal(i))
            {
                if (_indInnerRings[i] == null)
                {
                    var ring = new Ellipse { Fill = Brushes.Transparent, Stroke = Brushes.Black, ZIndex = 2 };
                    _indInnerRings[i] = ring;
                    canvas.Children.Add(ring);
                }
                double rDiam = indDiam * 0.82;
                var ir = _indInnerRings[i]!;
                ir.Width = rDiam; ir.Height = rDiam;
                ir.StrokeThickness = Math.Max(0.5, indDiam * 0.030);
                Canvas.SetLeft(ir, indCX - rDiam / 2);
                Canvas.SetTop(ir,  _laneY[i] - rDiam / 2);
            }

            var label = _laneIndLabels[i]!;
            label.Text     = _lanes[i].Label;
            label.FontSize = fontSize;
            Canvas.SetLeft(label, lblX);
            Canvas.SetTop(label,  _laneY[i] - fontSize / 2 - 1);
        }

        // HH open/closed state
        if (_hhStateLabel == null)
        {
            _hhStateLabel = new TextBlock { Text = "●", Foreground = _laneBrushes[0] };
            canvas.Children.Add(_hhStateLabel);
        }
        _hhStateLabel.FontSize = fontSize;
        Canvas.SetLeft(_hhStateLabel, lblX + 28);
        Canvas.SetTop(_hhStateLabel,  _laneY[0] - fontSize / 2 - 1);

        // Kit pads at bottom (visual reference + hit-ring anchor)
        for (int i = 0; i < LaneCount; i++)
        {
            if (_pads[i] == null)
            {
                var pad = new Ellipse { ZIndex = 0 };
                var lbl = new TextBlock { Foreground = Brushes.White, TextAlignment = TextAlignment.Center, ZIndex = 3 };
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

            // Cymbal inner black ring
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

            // Label centered inside the circle
            double padFontSize = Math.Clamp(diam * 0.22, 10, 16);
            var l = _padLabels[i]!;
            l.Text     = _lanes[i].Label;
            l.FontSize = padFontSize;
            l.Width    = diam;
            Canvas.SetLeft(l, _padCX[i] - diam / 2);
            Canvas.SetTop(l,  _padCY[i] - padFontSize * 0.65);
        }

        var now = DateTime.Now;
        foreach (var (visual, hitTime, lane) in _activeCues)
        {
            var (cx, cy) = CuePosition(hitTime, now, lane);
            double cW = _laneSpacing * 0.35;
            double cH = _laneSpacing * 0.65;
            Canvas.SetLeft(visual, cx - cW / 2);
            Canvas.SetTop(visual,  cy - cH / 2);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var canvas = CueCanvas;
        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;

        if (_pads[0] == null && w > 0 && h > 0)
            RebuildLayout();

        if (w <= 0 || h <= 0) return;

        var now       = DateTime.Now;
        var lookAhead = now + TimeSpan.FromMilliseconds(TravelMs);

        if (Engine != null)
        {
            foreach (var cue in Engine.DrainCues(lookAhead))
            {
                double cW = _laneSpacing * 0.35;
                double cH = _laneSpacing * 0.65;
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
                _activeCues.Add((rect, cue.ScheduledHitTime, cue.Lane));
            }
            Engine.DrainGridLines(lookAhead);
        }

        // Metronome audio
        if (Metronome?.IsEnabled == true && Audio != null)
        {
            foreach (var _ in Metronome.DrainTicks(now))
                Audio.PlayMetronomeClick();
        }

        // Update and expire note cues
        for (int i = _activeCues.Count - 1; i >= 0; i--)
        {
            var (visual, hitTime, lane) = _activeCues[i];
            var (cx, cy) = CuePosition(hitTime, now, lane);
            if (cx > _hitZoneX + _laneSpacing * 0.5)
            {
                canvas.Children.Remove(visual);
                _activeCues.RemoveAt(i);
            }
            else
            {
                double cW = _laneSpacing * 0.35;
                double cH = _laneSpacing * 0.65;
                Canvas.SetLeft(visual, cx - cW / 2);
                Canvas.SetTop(visual,  cy - cH / 2);
            }
        }

        // Pad and indicator flash
        for (int i = 0; i < LaneCount; i++)
        {
            bool shouldFlash = now < _padFlashUntil[i];
            if (shouldFlash != _isFlashing[i])
            {
                _isFlashing[i] = shouldFlash;
                IBrush fill = shouldFlash ? Brushes.White : _laneBrushes[i];
                if (_pads[i]           != null) _pads[i]!.Fill           = fill;
                if (_laneIndicators[i] != null) _laneIndicators[i]!.Fill = fill;
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

        // Scrolling beat-subdivision grid lines
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
            double lineTop   = _laneY[0]            - _laneSpacing / 2;
            double lineBot   = _laneY[LaneCount - 1] + _laneSpacing / 2;

            // Range: lines that could be on-screen
            int firstBeat = Math.Max(0, (int)((elapsed - TravelMs * 0.25) / eighthMs));
            int lastBeat  = (int)((elapsed + TravelMs)  / eighthMs) + 1;

            for (int beat = firstBeat; beat <= lastBeat && poolIdx < GridLinePoolSize; beat++)
            {
                double opacity = (beat % 8 == 0) ? 0.0    // 1/1 — invisible
                               : (beat % 4 == 0) ? 0.5    // 1/2
                               : (beat % 2 == 0) ? 0.25   // 1/4
                               :                   0.125;  // 1/8

                if (opacity == 0) continue;

                DateTime beatTime    = Engine.SongStartTime.AddMilliseconds(beat * eighthMs);
                double   remaining   = (beatTime - now).TotalMilliseconds;
                double   progress    = 1.0 - remaining / TravelMs;
                double   x           = -20 + progress * (_hitZoneX + 20);

                if (x < -2 || x > _canvasW + 2) continue;

                var line = _gridLinePool[poolIdx++];
                line.StartPoint = new Point(x, lineTop);
                line.EndPoint   = new Point(x, lineBot);
                line.Opacity    = opacity;
            }
        }

        // Hide unused pool lines
        for (int k = poolIdx; k < GridLinePoolSize; k++)
            _gridLinePool[k].Opacity = 0;
    }

    private (double X, double Y) CuePosition(DateTime hitTime, DateTime now, int lane)
    {
        double remainingMs = (hitTime - now).TotalMilliseconds;
        double progress    = 1.0 - remainingMs / TravelMs;
        double x = -20 + progress * (_hitZoneX + 20);
        double y = _laneY[lane];
        return (x, y);
    }

    private void OnDrumHit(object? sender, DrumHit hit)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var now = DateTime.Now;
            for (int i = 0; i < LaneCount; i++)
            {
                if (Array.IndexOf(_lanes[i].NoteNumbers, hit.NoteNumber) < 0) continue;

                _padFlashUntil[i] = now.AddMilliseconds(150);

                bool onBeat = Metronome?.IsEnabled == true && Metronome.IsOnBeat(now);

                for (int j = 0; j < _activeCues.Count; j++)
                {
                    var (_, hitTime, lane) = _activeCues[j];
                    if (lane == i && Math.Abs((hitTime - now).TotalMilliseconds) <= HitWindowMs)
                    {
                        SpawnHitRing(i, now, onBeat);
                        break;
                    }
                }
                break;
            }
        });
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
}
