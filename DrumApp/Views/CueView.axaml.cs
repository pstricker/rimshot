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

    // Layout cache
    private double _laneWidth, _hitZoneY, _padDiameter, _cueWidth, _cueHeight;
    private readonly double[] _laneX = new double[LaneCount];

    // Fixed canvas elements
    private readonly Ellipse?[] _pads = new Ellipse?[LaneCount];
    private readonly TextBlock?[] _padLabels = new TextBlock?[LaneCount];
    private readonly Rectangle?[] _laneGuides = new Rectangle?[LaneCount];
    private Rectangle? _hitZoneRect;

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

        _laneWidth = w / LaneCount;
        _hitZoneY = h * HitZoneFraction;
        _padDiameter = Math.Max(24, _laneWidth * 0.55);
        _cueWidth = _laneWidth * 0.70;
        _cueHeight = Math.Max(12, h * 0.03);

        for (int i = 0; i < LaneCount; i++)
            _laneX[i] = i * _laneWidth + _laneWidth / 2.0;

        // Hit zone line
        if (_hitZoneRect == null)
        {
            _hitZoneRect = new Rectangle { Height = 2, Fill = Brushes.White };
            canvas.Children.Add(_hitZoneRect);
        }
        _hitZoneRect.Width = w;
        Canvas.SetLeft(_hitZoneRect, 0);
        Canvas.SetTop(_hitZoneRect, _hitZoneY);

        // Lane guides
        for (int i = 0; i < LaneCount; i++)
        {
            if (_laneGuides[i] == null)
            {
                var guide = new Rectangle { Width = 1, Fill = new SolidColorBrush(Color.Parse("#33FFFFFF")) };
                _laneGuides[i] = guide;
                canvas.Children.Add(guide);
            }
            _laneGuides[i]!.Height = _hitZoneY;
            Canvas.SetLeft(_laneGuides[i]!, _laneX[i]);
            Canvas.SetTop(_laneGuides[i]!, 0);
        }

        // Pads and labels
        double fontSize = Math.Clamp(_laneWidth * 0.18, 10, 18);
        double padTopY = _hitZoneY + 10;

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
            p.Width = _padDiameter;
            p.Height = _padDiameter;
            p.Fill = _isFlashing[i] ? Brushes.White : _laneBrushes[i];
            Canvas.SetLeft(p, _laneX[i] - _padDiameter / 2.0);
            Canvas.SetTop(p, padTopY);

            var l = _padLabels[i]!;
            l.Text = _lanes[i].Label;
            l.FontSize = fontSize;
            l.Width = _laneWidth;
            Canvas.SetLeft(l, i * _laneWidth);
            Canvas.SetTop(l, padTopY + _padDiameter + 4);
        }

        // HH open/closed state label (below HH pad label)
        if (_hhStateLabel == null)
        {
            _hhStateLabel = new TextBlock
            {
                Text = "●",
                Foreground = _laneBrushes[0],
                TextAlignment = TextAlignment.Center,
            };
            canvas.Children.Add(_hhStateLabel);
        }
        _hhStateLabel.FontSize = fontSize;
        _hhStateLabel.Width = _laneWidth;
        Canvas.SetLeft(_hhStateLabel, 0);
        Canvas.SetTop(_hhStateLabel, padTopY + _padDiameter + 4 + fontSize + 4);

        // Reposition active scrolling elements
        var now = DateTime.Now;
        foreach (var (visual, hitTime, _) in _activeCues)
            Canvas.SetTop(visual, ComputeY(hitTime, now));
        foreach (var (visual, hitTime) in _activeGridLines)
        {
            visual.Width = w;
            Canvas.SetTop(visual, ComputeY(hitTime, now));
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var canvas = CueCanvas;
        double w = canvas.Bounds.Width;
        double h = canvas.Bounds.Height;

        if (_hitZoneRect == null && w > 0 && h > 0)
            RebuildLayout();

        if (w <= 0 || h <= 0) return;

        var now = DateTime.Now;
        var lookAhead = now + TimeSpan.FromMilliseconds(TravelMs);

        if (Engine != null)
        {
            foreach (var cue in Engine.DrainCues(lookAhead))
            {
                var rect = new Rectangle
                {
                    Width = _cueWidth,
                    Height = _cueHeight,
                    Fill = _laneBrushes[cue.Lane],
                    RadiusX = 4,
                    RadiusY = 4,
                };
                Canvas.SetLeft(rect, _laneX[cue.Lane] - _cueWidth / 2.0);
                Canvas.SetTop(rect, ComputeY(cue.ScheduledHitTime, now));
                canvas.Children.Add(rect);
                _activeCues.Add((rect, cue.ScheduledHitTime, cue.Lane));
            }

            foreach (var hitTime in Engine.DrainGridLines(lookAhead))
            {
                var rect = new Rectangle
                {
                    Width = w,
                    Height = 1,
                    Fill = new SolidColorBrush(Color.Parse("#33FFFFFF")),
                };
                Canvas.SetLeft(rect, 0);
                Canvas.SetTop(rect, ComputeY(hitTime, now));
                canvas.Children.Add(rect);
                _activeGridLines.Add((rect, hitTime));
            }
        }

        // Metronome audio — drain ticks due at or before now
        if (Metronome?.IsEnabled == true && Audio != null)
        {
            foreach (var _ in Metronome.DrainTicks(now))
                Audio.PlayMetronomeClick();
        }

        double expireY = _hitZoneY + 48;

        for (int i = _activeCues.Count - 1; i >= 0; i--)
        {
            var (visual, hitTime, _) = _activeCues[i];
            double y = ComputeY(hitTime, now);
            if (y > expireY)
            {
                canvas.Children.Remove(visual);
                _activeCues.RemoveAt(i);
            }
            else
            {
                Canvas.SetTop(visual, y);
            }
        }

        for (int i = _activeGridLines.Count - 1; i >= 0; i--)
        {
            var (visual, hitTime) = _activeGridLines[i];
            double y = ComputeY(hitTime, now);
            if (y > expireY)
            {
                canvas.Children.Remove(visual);
                _activeGridLines.RemoveAt(i);
            }
            else
            {
                Canvas.SetTop(visual, y);
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
        double padTopY = _hitZoneY + 10;
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
            double size = _padDiameter * (1.0 + progress * 1.4);
            double padCenterY = padTopY + _padDiameter / 2.0;
            ring.Width = size;
            ring.Height = size;
            ring.Opacity = 1.0 - progress;
            Canvas.SetLeft(ring, _laneX[lane] - size / 2.0);
            Canvas.SetTop(ring, padCenterY - size / 2.0);
        }
    }

    private double ComputeY(DateTime hitTime, DateTime now)
    {
        double remainingMs = (hitTime - now).TotalMilliseconds;
        return _hitZoneY * (1.0 - remainingMs / TravelMs);
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
            Fill = Brushes.Transparent,
            Stroke = onBeat ? Brushes.LimeGreen : _laneBrushes[lane],
            StrokeThickness = onBeat ? 4 : 3,
            Width = _padDiameter,
            Height = _padDiameter,
        };
        double padTopY = _hitZoneY + 10;
        double padCenterY = padTopY + _padDiameter / 2.0;
        Canvas.SetLeft(ring, _laneX[lane] - _padDiameter / 2.0);
        Canvas.SetTop(ring, padCenterY - _padDiameter / 2.0);
        CueCanvas.Children.Add(ring);
        _hitRings.Add((ring, now, lane));
    }
}
