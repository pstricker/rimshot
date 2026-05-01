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
    private const int LaneCount = 7;
    private const double HitWindowMs = 150.0;
    private const double RingDurationMs = 350.0;

    private readonly IReadOnlyList<DrumLane> _lanes = DrumLane.StandardKit();
    private readonly SolidColorBrush[] _laneBrushes;
    private DispatcherTimer? _timer;

    // Kit layout — compressed toward bottom-center, matching top-down drum kit view
    // (Xf = fraction of canvas width, Yf = fraction of canvas height, Df = fraction of canvas height)
    private static readonly (double Xf, double Yf, double Df)[] _padLayout =
    [
        (0.33, 0.87, 0.110), // HH  — left, lower
        (0.36, 0.73, 0.110), // CR  — upper-left
        (0.41, 0.91, 0.110), // SN  — front-left, low
        (0.47, 0.72, 0.100), // TM-hi — upper-center (clear of BD)
        (0.50, 0.86, 0.150), // BD  — center, large
        (0.60, 0.88, 0.110), // TM-floor — right, raised
        (0.63, 0.73, 0.110), // RD  — upper-right
    ];
    private readonly double[] _padCX     = new double[LaneCount];
    private readonly double[] _padCY     = new double[LaneCount];
    private readonly double[] _padDiamPx = new double[LaneCount];
    private double _canvasW;

    // Fixed canvas elements
    private readonly Ellipse?[] _pads = new Ellipse?[LaneCount];
    private readonly TextBlock?[] _padLabels = new TextBlock?[LaneCount];

    // Flash state
    private readonly DateTime[] _padFlashUntil = new DateTime[LaneCount];
    private readonly bool[] _isFlashing = new bool[LaneCount];

    // Active scrolling elements — Lane stored for hit accuracy check
    private readonly List<(Rectangle Visual, DateTime HitTime, int Lane)> _activeCues = [];
    private readonly List<(Rectangle Visual, DateTime HitTime)> _activeGridLines = [];

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

        if (Midi != null)
            Midi.DrumHitReceived += OnDrumHit;

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

        if (Midi != null)
            Midi.DrumHitReceived -= OnDrumHit;
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

        double fontSize = Math.Clamp(h * 0.020, 10, 13);

        for (int i = 0; i < LaneCount; i++)
        {
            if (_pads[i] == null)
            {
                var pad = new Ellipse();
                var lbl = new TextBlock { Foreground = Brushes.White, TextAlignment = TextAlignment.Center };
                _pads[i] = pad;
                _padLabels[i] = lbl;
                canvas.Children.Add(pad);
                canvas.Children.Add(lbl);
            }

            var p = _pads[i]!;
            p.Width  = _padDiamPx[i];
            p.Height = _padDiamPx[i];
            p.Fill   = _isFlashing[i] ? Brushes.White : _laneBrushes[i];
            Canvas.SetLeft(p, _padCX[i] - _padDiamPx[i] / 2.0);
            Canvas.SetTop(p,  _padCY[i] - _padDiamPx[i] / 2.0);

            var l = _padLabels[i]!;
            l.Text     = _lanes[i].Label;
            l.FontSize = fontSize;
            l.Width    = 50;
            Canvas.SetLeft(l, _padCX[i] - 25);
            Canvas.SetTop(l,  _padCY[i] + _padDiamPx[i] / 2.0 + 2);
        }

        if (_hhStateLabel == null)
        {
            _hhStateLabel = new TextBlock { Text = "●", Foreground = _laneBrushes[0], TextAlignment = TextAlignment.Center };
            canvas.Children.Add(_hhStateLabel);
        }
        _hhStateLabel.FontSize = fontSize;
        _hhStateLabel.Width    = 50;
        Canvas.SetLeft(_hhStateLabel, _padCX[0] - 25);
        Canvas.SetTop(_hhStateLabel,  _padCY[0] + _padDiamPx[0] / 2.0 + fontSize + 4);

        var now = DateTime.Now;
        foreach (var (visual, hitTime, lane) in _activeCues)
        {
            var (cx, cy) = CuePosition(hitTime, now, lane);
            Canvas.SetLeft(visual, cx - _padDiamPx[lane] * 0.45);
            Canvas.SetTop(visual,  cy - _padDiamPx[lane] * 0.15);
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

        var now = DateTime.Now;
        var lookAhead = now + TimeSpan.FromMilliseconds(TravelMs);

        if (Engine != null)
        {
            foreach (var cue in Engine.DrainCues(lookAhead))
            {
                double cW = _padDiamPx[cue.Lane] * 0.90;
                double cH = _padDiamPx[cue.Lane] * 0.30;
                var rect = new Rectangle
                {
                    Width   = cW,
                    Height  = cH,
                    Fill    = _laneBrushes[cue.Lane],
                    RadiusX = 4,
                    RadiusY = 4,
                };
                var (cx, cy) = CuePosition(cue.ScheduledHitTime, now, cue.Lane);
                Canvas.SetLeft(rect, cx - cW / 2);
                Canvas.SetTop(rect,  cy - cH / 2);
                canvas.Children.Add(rect);
                _activeCues.Add((rect, cue.ScheduledHitTime, cue.Lane));
            }

            // Drain grid lines but don't render
            Engine.DrainGridLines(lookAhead);
        }

        // Metronome audio
        if (Metronome?.IsEnabled == true && Audio != null)
        {
            foreach (var _ in Metronome.DrainTicks(now))
                Audio.PlayMetronomeClick();
        }

        // Update and expire cues
        for (int i = _activeCues.Count - 1; i >= 0; i--)
        {
            var (visual, hitTime, lane) = _activeCues[i];
            var (cx, cy) = CuePosition(hitTime, now, lane);
            if (cy > _padCY[lane] + _padDiamPx[lane])
            {
                canvas.Children.Remove(visual);
                _activeCues.RemoveAt(i);
            }
            else
            {
                double cW = _padDiamPx[lane] * 0.90;
                double cH = _padDiamPx[lane] * 0.30;
                Canvas.SetLeft(visual, cx - cW / 2);
                Canvas.SetTop(visual,  cy - cH / 2);
            }
        }

        // Pad flash
        for (int i = 0; i < LaneCount; i++)
        {
            if (_pads[i] == null) continue;
            bool shouldFlash = now < _padFlashUntil[i];
            if (shouldFlash != _isFlashing[i])
            {
                _isFlashing[i] = shouldFlash;
                _pads[i]!.Fill = shouldFlash ? Brushes.White : _laneBrushes[i];
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
    }

    private (double X, double Y) CuePosition(DateTime hitTime, DateTime now, int lane)
    {
        double remainingMs = (hitTime - now).TotalMilliseconds;
        double progress = 1.0 - remainingMs / TravelMs; // 0 = origin at top, 1 = at pad
        // Spread origins across full canvas width, amplified outward from center
        const double SpreadFactor = 3.0;
        double originX = Math.Clamp(_canvasW / 2 + (_padCX[lane] - _canvasW / 2) * SpreadFactor,
                                    _canvasW * 0.03, _canvasW * 0.97);
        double x = originX + progress * (_padCX[lane] - originX);
        double y = -20    + progress * (_padCY[lane] + 20);
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

                // Check if any active cue for this lane is within the hit window
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
