using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Rimshot.Core.Models;
using Rimshot.Core.Services;
using Rimshot.Services;

namespace Rimshot.Views;

/// <summary>
/// Mini piano-roll timeline that lets the user drag-create / drag-refine a
/// loop region snapped to the 1/16 grid. Click-and-drag on empty timeline
/// creates a new loop; press-and-drag on a handle refines an existing one.
///
/// All visual polish (snap indicator, hover, fades, playhead pulse, bar
/// labels with auto-hide, empty-state hint, tooltips) lives in this file.
/// </summary>
public partial class SongTimelineView : UserControl
{
    // ── Layout constants ─────────────────────────────────────────────────────
    private const double DefaultPxPerEighth = 24.0;     // → 12px per 1/16
    private const double LaneRowHeight      = 6.0;
    private const double TopRulerHeight     = 18.0;     // bar-number band
    private const double TimelineBodyTop    = TopRulerHeight + 2;
    private const double HandleHitWidth     = 14.0;
    private const double HandleVisualWidth  = 6.0;
    private const double FadeMs             = 150.0;
    private const double PulseMs            = 220.0;
    private const double MinBarLabelGapPx   = 32.0;     // hide labels if bars too tight

    // ── Brushes (mirror AppStyles palette) ───────────────────────────────────
    private static readonly IBrush GoldBrush      = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush GoldHoverBrush = new SolidColorBrush(Color.Parse("#FFEA70"));
    private static readonly IBrush PinkBrush      = new SolidColorBrush(Color.Parse("#FF1493"));
    private static readonly IBrush CyanBrush      = new SolidColorBrush(Color.Parse("#00D9FF"));
    private static readonly IBrush MutedBrush     = new SolidColorBrush(Color.Parse("#888888"));
    private static readonly IBrush GridLineBrush  = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush SnapBrush      = new SolidColorBrush(Color.Parse("#FFD700"));

    private static readonly FontFamily BebasNeue =
        new("avares://Rimshot.Core/Assets/Fonts#Bebas Neue");

    // ── State ────────────────────────────────────────────────────────────────
    public CueEngine?            Engine { get; set; }
    public LoopSelectionService? Loop   { get; set; }

    private Song?  _song;
    private double _pxPerEighth = DefaultPxPerEighth;

    // Total canvas width in pixels. Cached during Rebuild so the eighth↔X
    // mirror helpers below can reference it without recomputing.
    private double _totalWidth;

    // Mirror helpers: timeline X axis is reversed relative to the natural
    // (eighths * px) order so that earlier-in-song bars sit on the RIGHT
    // (matching CueView's scroll direction where future is left, "now" is
    // right). Bar 1 → right edge; last bar → left edge; playhead moves
    // right → left as the song plays.
    private double EighthsToX(double eighths) => _totalWidth - eighths * _pxPerEighth;
    private double XToEighths(double x)       => (_totalWidth - x) / _pxPerEighth;

    // Drag state machine: "None" (idle), "Creating" (initial press → drag),
    // "MoveStart" / "MoveEnd" (refining an existing handle).
    private enum DragMode { None, Creating, MoveStart, MoveEnd }
    private DragMode _dragMode;
    private double   _dragAnchorEighths;          // for Creating: starting eighth
    private double?  _liveSnapEighths;            // for snap-grid indicator
    private bool     _hoverStart, _hoverEnd;

    // Visuals
    private readonly List<Control> _gridChildren  = new();
    private readonly List<Control> _noteChildren  = new();
    private readonly List<Control> _labelChildren = new();
    private Rectangle? _loopOverlay;
    private Rectangle? _handleStart;
    private Rectangle? _handleEnd;
    private Line?      _playhead;
    private Ellipse?   _playheadPulse;
    private Line?      _snapIndicator;
    private TextBlock? _emptyHint;

    private DispatcherTimer? _animTimer;
    private DateTime         _overlayFadeStart;
    private bool             _overlayFadingIn;
    private bool             _overlayFadingOut;
    private double           _lastPlayheadEighths = -1;
    private DateTime         _playheadPulseStart  = DateTime.MinValue;

    public SongTimelineView()
    {
        InitializeComponent();

        TimelineCanvas.PointerPressed     += OnCanvasPressed;
        TimelineCanvas.PointerMoved       += OnCanvasMoved;
        TimelineCanvas.PointerReleased    += OnCanvasReleased;
        TimelineCanvas.PointerExited      += OnCanvasExited;
        SizeChanged                       += (_, _) => Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Loop is not null) Loop.LoopChanged += OnLoopChanged;
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
        UpdateClearLoopVisibility();
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Loop is not null) Loop.LoopChanged -= OnLoopChanged;
        _animTimer?.Stop();
        _animTimer = null;
    }

    public void SetSong(Song song)
    {
        _song = song;
        Rebuild();
        // Mirrored layout: bar 1 lives at the RIGHT edge of the canvas, so
        // scroll the viewport to that edge once the layout has measured.
        Dispatcher.UIThread.Post(ScrollToBarOne, DispatcherPriority.Background);
    }

    private void ScrollToBarOne()
    {
        if (TimelineScroll is null) return;
        double max = Math.Max(0, _totalWidth - TimelineScroll.Viewport.Width);
        TimelineScroll.Offset = new Vector(max, TimelineScroll.Offset.Y);
    }

    private void OnLoopChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Trigger fade-in when the loop appears, fade-out when cleared.
            bool nowActive = Loop?.IsActive == true;
            bool wasShown  = _loopOverlay is { Opacity: > 0 };

            if (nowActive && !wasShown)
            {
                _overlayFadingIn  = true;
                _overlayFadingOut = false;
                _overlayFadeStart = DateTime.Now;
            }
            else if (!nowActive && wasShown)
            {
                _overlayFadingOut = true;
                _overlayFadingIn  = false;
                _overlayFadeStart = DateTime.Now;
            }
            UpdateClearLoopVisibility();
            Rebuild();
        });

    private void UpdateClearLoopVisibility()
    {
        if (ClearLoopButton is null) return;
        ClearLoopButton.IsVisible = Loop?.IsActive == true;
    }

    private void OnClearLoopClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Loop?.ClearLoop();

    // ── Layout / rebuild ─────────────────────────────────────────────────────
    private void Rebuild()
    {
        var canvas = TimelineCanvas;
        if (canvas is null) return;

        canvas.Children.Clear();
        _gridChildren.Clear();
        _noteChildren.Clear();
        _labelChildren.Clear();
        _loopOverlay = null;
        _handleStart = null;
        _handleEnd   = null;
        _playhead    = null;
        _playheadPulse = null;
        _snapIndicator = null;
        _emptyHint   = null;

        if (_song is null || _song.TotalEighths <= 0)
        {
            canvas.Width = 0;
            _totalWidth  = 0;
            return;
        }

        double totalWidth = _song.TotalEighths * _pxPerEighth;
        canvas.Width = totalWidth;
        _totalWidth  = totalWidth;

        // Each Draw* step is best-effort: a failure in one (e.g. brush
        // creation, font fallback, control measurement during pre-attach
        // construction) must not bubble out of Rebuild and abort the caller's
        // load flow — that previously caused unrelated UI state (the
        // "BACKING TRACK" checkbox visibility) to silently regress whenever
        // a MIDI file with melodic content was selected.
        SafeDraw(() => DrawGrid(totalWidth),       nameof(DrawGrid));
        SafeDraw(() => DrawBarLabels(totalWidth),  nameof(DrawBarLabels));
        SafeDraw(DrawNotes,                        nameof(DrawNotes));
        SafeDraw(DrawLoopOverlay,                  nameof(DrawLoopOverlay));
        SafeDraw(DrawPlayhead,                     nameof(DrawPlayhead));
        SafeDraw(DrawSnapIndicator,                nameof(DrawSnapIndicator));
        SafeDraw(() => DrawEmptyHint(totalWidth),  nameof(DrawEmptyHint));
    }

    private static void SafeDraw(Action draw, string name)
    {
        try { draw(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"SongTimelineView.{name} failed: {ex}");
        }
    }

    private void DrawGrid(double totalWidth)
    {
        if (_song is null) return;
        double height = TimelineCanvas.Height;
        double bodyTop = TimelineBodyTop;
        double bodyBot = height;

        // Step in 1/16 (0.5 eighths) increments
        for (double e = 0; e <= _song.TotalEighths + 1e-6; e += 0.5)
        {
            // Reuse the CueView opacity scheme: bars=brightest, beats medium,
            // 1/8 lighter, 1/16 lightest.
            double opacity = (Math.Abs(e % 8) < 1e-6) ? 0.55
                           : (Math.Abs(e % 4) < 1e-6) ? 0.30
                           : (Math.Abs(e % 2) < 1e-6) ? 0.16
                           : (Math.Abs(e % 1) < 1e-6) ? 0.10
                           :                            0.06;

            double x = EighthsToX(e);
            var line = new Line
            {
                StartPoint      = new Point(x, bodyTop),
                EndPoint        = new Point(x, bodyBot),
                Stroke          = GridLineBrush,
                StrokeThickness = (Math.Abs(e % 8) < 1e-6) ? 1.5 : 1.0,
                Opacity         = opacity,
                IsHitTestVisible = false,
                ZIndex          = -10,
            };
            TimelineCanvas.Children.Add(line);
            _gridChildren.Add(line);
        }
    }

    private void DrawBarLabels(double totalWidth)
    {
        if (_song is null) return;
        double pxPerBar = 8 * _pxPerEighth;
        bool   showLabels = pxPerBar >= MinBarLabelGapPx;

        int barCount = (int)Math.Ceiling(_song.TotalEighths / 8.0);
        for (int bar = 0; bar < barCount; bar++)
        {
            var label = new TextBlock
            {
                Text             = (bar + 1).ToString(),
                FontFamily       = BebasNeue,
                FontSize         = 12,
                Foreground       = MutedBrush,
                Opacity          = showLabels ? 0.85 : 0.0,
                IsHitTestVisible = false,
                ZIndex           = 5,
            };
            // Mirror: bar `n`'s region spans from EighthsToX((n+1)*8) on the
            // left to EighthsToX(n*8) on the right. Place the label 4 px in
            // from the LEFT edge of that region so labels still read 1, 2,
            // 3… going right → left across the timeline.
            Canvas.SetLeft(label, EighthsToX((bar + 1) * 8) + 4);
            Canvas.SetTop(label, 1);
            TimelineCanvas.Children.Add(label);
            _labelChildren.Add(label);
        }
    }

    private void DrawNotes()
    {
        if (_song is null) return;
        var lanes = DrumLane.StandardKit();
        // Resolve which lanes are actually used by this song so we can pack rows.
        var usedLanes = new List<int>();
        var seen = new HashSet<int>();
        foreach (var n in _song.Notes)
            if (seen.Add(n.Lane)) usedLanes.Add(n.Lane);
        usedLanes.Sort();

        if (usedLanes.Count == 0) return;

        // Available vertical space for note rows
        double rowsTop    = TimelineBodyTop + 4;
        double rowsBot    = TimelineCanvas.Height - 6;
        double rowHeight  = Math.Max(3.0, Math.Min(LaneRowHeight,
                                (rowsBot - rowsTop) / usedLanes.Count));
        var laneToRow = new Dictionary<int, int>();
        for (int i = 0; i < usedLanes.Count; i++) laneToRow[usedLanes[i]] = i;

        double noteWidth = Math.Max(2.0, _pxPerEighth * 0.4);
        foreach (var note in _song.Notes)
        {
            if (!laneToRow.TryGetValue(note.Lane, out int row)) continue;
            // Mirror: anchor the rect so its RIGHT edge sits at the
            // mirrored note position (matches original behavior where the
            // LEFT edge sat at the unmirrored note position).
            double x = EighthsToX(note.OffsetInEighths) - noteWidth;
            double y = rowsTop + row * rowHeight;
            var rect = new Rectangle
            {
                Width  = noteWidth,
                Height = Math.Max(2.0, rowHeight - 1.5),
                Fill   = new SolidColorBrush(lanes[note.Lane].Color),
                IsHitTestVisible = false,
                ZIndex = -2,
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            TimelineCanvas.Children.Add(rect);
            _noteChildren.Add(rect);
        }
    }

    private void DrawLoopOverlay()
    {
        if (Loop is null || !Loop.IsActive) return;

        double height = TimelineCanvas.Height;
        // Mirror: start (earlier in song) → right side; end (later) → left side.
        double startX = EighthsToX(Loop.StartEighths);
        double endX   = EighthsToX(Loop.EndEighths);
        double width  = Math.Max(1.0, startX - endX);

        _loopOverlay = new Rectangle
        {
            Width  = width,
            Height = height - TimelineBodyTop,
            Fill   = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0x14, 0x93)), // ~30% pink
            IsHitTestVisible = false,
            ZIndex = 1,
        };
        Canvas.SetLeft(_loopOverlay, endX);
        Canvas.SetTop(_loopOverlay, TimelineBodyTop);
        TimelineCanvas.Children.Add(_loopOverlay);

        // Subtle bordered top edge for visual definition
        var topEdge = new Line
        {
            StartPoint = new Point(endX,   TimelineBodyTop),
            EndPoint   = new Point(startX, TimelineBodyTop),
            Stroke     = PinkBrush,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            ZIndex     = 2,
        };
        TimelineCanvas.Children.Add(topEdge);

        // Drag handles — start handle on the right, end handle on the left.
        _handleStart = MakeHandle(startX, height);
        _handleEnd   = MakeHandle(endX,   height);
        TimelineCanvas.Children.Add(_handleStart);
        TimelineCanvas.Children.Add(_handleEnd);
        ApplyHandleHover();

        // Apply current fade target opacity
        double targetOpacity = ComputeOverlayOpacity();
        _loopOverlay.Opacity = targetOpacity;
        topEdge.Opacity      = targetOpacity;
        _handleStart.Opacity = targetOpacity;
        _handleEnd.Opacity   = targetOpacity;
    }

    private Rectangle MakeHandle(double x, double height)
    {
        var h = new Rectangle
        {
            Width  = HandleVisualWidth,
            Height = height - TimelineBodyTop,
            Fill   = GoldBrush,
            ZIndex = 3,
            IsHitTestVisible = false,    // hit-tests done on the canvas to keep one-shot drag
        };
        Canvas.SetLeft(h, x - HandleVisualWidth / 2);
        Canvas.SetTop(h, TimelineBodyTop);
        return h;
    }

    private void DrawPlayhead()
    {
        if (Engine is null) return;
        _playhead = new Line
        {
            Stroke           = CyanBrush,
            StrokeThickness  = 1,
            IsHitTestVisible = false,
            ZIndex           = 4,
            Opacity          = 0,
        };
        TimelineCanvas.Children.Add(_playhead);

        _playheadPulse = new Ellipse
        {
            Width            = 14,
            Height           = 14,
            Fill             = Brushes.Transparent,
            Stroke           = CyanBrush,
            StrokeThickness  = 2,
            IsHitTestVisible = false,
            ZIndex           = 4,
            Opacity          = 0,
        };
        TimelineCanvas.Children.Add(_playheadPulse);
    }

    private void DrawSnapIndicator()
    {
        _snapIndicator = new Line
        {
            Stroke           = SnapBrush,
            StrokeThickness  = 1.5,
            StrokeDashArray  = new AvaloniaList<double> { 3, 3 },
            IsHitTestVisible = false,
            Opacity          = 0,
            ZIndex           = 6,
        };
        TimelineCanvas.Children.Add(_snapIndicator);
    }

    private void DrawEmptyHint(double totalWidth)
    {
        bool hasLoop  = Loop?.IsActive == true;
        bool playing  = Engine?.State == CueEngineState.Running;
        if (hasLoop || playing) return;

        _emptyHint = new TextBlock
        {
            Text       = "Click and drag to set a practice loop",
            FontFamily = BebasNeue,
            FontSize   = 14,
            Foreground = MutedBrush,
            Opacity    = 0.55,
            IsHitTestVisible = false,
            ZIndex     = 7,
        };
        // Center within the visible viewport (use canvas width)
        Canvas.SetLeft(_emptyHint, Math.Max(8, totalWidth / 2 - 110));
        Canvas.SetTop(_emptyHint,  TimelineBodyTop + (TimelineCanvas.Height - TimelineBodyTop) / 2 - 10);
        TimelineCanvas.Children.Add(_emptyHint);
    }

    // ── Pointer interaction ──────────────────────────────────────────────────
    private void OnCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_song is null || Loop is null) return;
        var pos = e.GetPosition(TimelineCanvas);
        if (pos.Y < TimelineBodyTop) return;
        double rawEighths = XToEighths(pos.X);
        rawEighths = Math.Clamp(rawEighths, 0, _song.TotalEighths);

        // Hit-test handles first (use mirrored X positions)
        if (Loop.IsActive)
        {
            double startX = EighthsToX(Loop.StartEighths);
            double endX   = EighthsToX(Loop.EndEighths);
            if (Math.Abs(pos.X - startX) <= HandleHitWidth / 2)
            {
                _dragMode = DragMode.MoveStart;
                e.Pointer.Capture(TimelineCanvas);
                return;
            }
            if (Math.Abs(pos.X - endX) <= HandleHitWidth / 2)
            {
                _dragMode = DragMode.MoveEnd;
                e.Pointer.Capture(TimelineCanvas);
                return;
            }
        }

        // Otherwise: begin creating a new loop range (snap anchor to grid).
        _dragMode = DragMode.Creating;
        _dragAnchorEighths = LoopSelectionService.SnapToGrid(rawEighths);
        _liveSnapEighths = _dragAnchorEighths;
        e.Pointer.Capture(TimelineCanvas);
        UpdateSnapIndicator();
    }

    private void OnCanvasMoved(object? sender, PointerEventArgs e)
    {
        if (_song is null || Loop is null) return;
        var pos = e.GetPosition(TimelineCanvas);
        double rawEighths = Math.Clamp(XToEighths(pos.X), 0, _song.TotalEighths);

        // Hover detection (no drag): cursor + handle highlight.
        if (_dragMode == DragMode.None)
        {
            UpdateHover(pos.X);
            return;
        }

        double snapped = LoopSelectionService.SnapToGrid(rawEighths);
        _liveSnapEighths = snapped;
        UpdateSnapIndicator();

        switch (_dragMode)
        {
            case DragMode.Creating:
                Loop.SetRange(_dragAnchorEighths, snapped);
                break;
            case DragMode.MoveStart:
                Loop.MoveStart(snapped);
                break;
            case DragMode.MoveEnd:
                Loop.MoveEnd(snapped);
                break;
        }
    }

    private void OnCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragMode == DragMode.None) return;
        _dragMode = DragMode.None;
        _liveSnapEighths = null;
        UpdateSnapIndicator();
        e.Pointer.Capture(null);
    }

    private void OnCanvasExited(object? sender, PointerEventArgs e)
    {
        if (_dragMode != DragMode.None) return;
        _hoverStart = false;
        _hoverEnd   = false;
        ApplyHandleHover();
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    private void UpdateHover(double mouseX)
    {
        if (Loop is null || !Loop.IsActive) return;
        double startX = EighthsToX(Loop.StartEighths);
        double endX   = EighthsToX(Loop.EndEighths);
        bool prevS = _hoverStart, prevE = _hoverEnd;
        _hoverStart = Math.Abs(mouseX - startX) <= HandleHitWidth / 2;
        _hoverEnd   = Math.Abs(mouseX - endX)   <= HandleHitWidth / 2;
        if (prevS != _hoverStart || prevE != _hoverEnd) ApplyHandleHover();

        Cursor = (_hoverStart || _hoverEnd)
            ? new Cursor(StandardCursorType.SizeWestEast)
            : new Cursor(StandardCursorType.Arrow);
    }

    private void ApplyHandleHover()
    {
        if (_handleStart is not null)
            _handleStart.Fill = _hoverStart ? GoldHoverBrush : GoldBrush;
        if (_handleEnd is not null)
            _handleEnd.Fill = _hoverEnd ? GoldHoverBrush : GoldBrush;
    }

    private void UpdateSnapIndicator()
    {
        if (_snapIndicator is null) return;
        if (_dragMode == DragMode.None || _liveSnapEighths is null)
        {
            _snapIndicator.Opacity = 0;
            return;
        }
        double x = EighthsToX(_liveSnapEighths.Value);
        _snapIndicator.StartPoint = new Point(x, TimelineBodyTop);
        _snapIndicator.EndPoint   = new Point(x, TimelineCanvas.Height);
        _snapIndicator.Opacity    = 0.75;
    }

    // ── Animation ────────────────────────────────────────────────────────────
    private double ComputeOverlayOpacity()
    {
        DateTime now = DateTime.Now;
        if (_overlayFadingIn)
        {
            double t = Math.Min(1.0, (now - _overlayFadeStart).TotalMilliseconds / FadeMs);
            if (t >= 1.0) _overlayFadingIn = false;
            return EaseOutCubic(t);
        }
        if (_overlayFadingOut)
        {
            double t = Math.Min(1.0, (now - _overlayFadeStart).TotalMilliseconds / FadeMs);
            if (t >= 1.0) { _overlayFadingOut = false; return 0.0; }
            return 1.0 - EaseOutCubic(t);
        }
        return Loop?.IsActive == true ? 1.0 : 0.0;
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    private void OnAnimTick(object? sender, EventArgs e)
    {
        // Drive overlay fade
        if (_loopOverlay is not null && (_overlayFadingIn || _overlayFadingOut))
        {
            double op = ComputeOverlayOpacity();
            _loopOverlay.Opacity = op;
            if (_handleStart is not null) _handleStart.Opacity = op;
            if (_handleEnd   is not null) _handleEnd.Opacity   = op;
            // If fade-out completed, rebuild to fully clear handles/overlay.
            if (!_overlayFadingOut && !_overlayFadingIn && Loop?.IsActive != true)
                Rebuild();
        }

        // Update playhead
        UpdatePlayhead();
    }

    private void UpdatePlayhead()
    {
        if (_playhead is null || Engine is null || _song is null) return;

        double pos = Engine.CurrentEighths;
        if (pos < 0 || Engine.State == CueEngineState.Stopped)
        {
            _playhead.Opacity      = 0;
            if (_playheadPulse is not null) _playheadPulse.Opacity = 0;
            _lastPlayheadEighths   = -1;
            return;
        }

        double x = EighthsToX(pos);
        _playhead.StartPoint = new Point(x, TimelineBodyTop);
        _playhead.EndPoint   = new Point(x, TimelineCanvas.Height);
        _playhead.Opacity    = 0.85;

        // Detect a wrap (position decreased significantly) → trigger pulse.
        if (Loop?.IsActive == true && _lastPlayheadEighths > 0
            && pos + 0.1 < _lastPlayheadEighths)
        {
            _playheadPulseStart = DateTime.Now;
        }
        _lastPlayheadEighths = pos;

        // Render pulse
        if (_playheadPulse is not null && _playheadPulseStart != DateTime.MinValue)
        {
            double t = (DateTime.Now - _playheadPulseStart).TotalMilliseconds / PulseMs;
            if (t >= 1.0)
            {
                _playheadPulse.Opacity = 0;
                _playheadPulseStart    = DateTime.MinValue;
            }
            else
            {
                double scale  = 1.0 + 1.4 * t;
                double size   = 14.0 * scale;
                _playheadPulse.Width  = size;
                _playheadPulse.Height = size;
                Canvas.SetLeft(_playheadPulse, x - size / 2);
                Canvas.SetTop(_playheadPulse,  TimelineBodyTop + 6);
                _playheadPulse.Opacity = 1.0 - t;
            }
        }

        // Auto-scroll to keep playhead visible
        var viewport = TimelineScroll.Viewport;
        var offset   = TimelineScroll.Offset;
        if (x < offset.X + 30)
            TimelineScroll.Offset = new Vector(Math.Max(0, x - 60), offset.Y);
        else if (x > offset.X + viewport.Width - 30)
            TimelineScroll.Offset = new Vector(x - viewport.Width + 60, offset.Y);
    }
}
