using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using boardzz.audio;

namespace boardzz.ui;

// Waveform with a start/end crop window.
// Press a handle (or anywhere: the nearest handle jumps there) and drag.
// Hold still for 2 s while pressed -> zooms in around the handle for fine placement
// (the knob fills up as the timer runs). Release -> zooms back out.
// Drag past an edge while zoomed -> the view pans.
public sealed class waveformView : FrameworkElement
{
    private enum dragHandle { none, start, end }

    private const double barWidth = 2;
    private const double barStep = 3;
    private const double handleHitPx = 10;
    private const double panEdgePx = 28;
    private const double minSelectionSeconds = 0.05;
    private const double zoomFraction = 0.1;
    private const double minZoomSeconds = 0.5;
    private const double maxZoomSeconds = 4;
    private const double easing = 0.3;
    private const double panSpeed = 0.025;
    private const double holdToZoomSeconds = 2.0;
    private const double holdMoveTolerancePx = 6;

    private static readonly Brush barBrush = frozen(0xFF, 0xF2, 0xF3, 0xF5);
    private static readonly Brush dimBarBrush = frozen(0xFF, 0x5C, 0x5F, 0x66);
    private static readonly Brush selectionBrush = frozen(0x26, 0x58, 0x65, 0xF2);
    private static readonly Brush handleBrush = frozen(0xFF, 0x58, 0x65, 0xF2);
    private static readonly Brush knobBrush = frozen(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Brush playheadBrush = frozen(0xFF, 0xF0, 0xB2, 0x32);
    private static readonly Pen zoomedKnobPen = frozenPen(0xFF, 0xFF, 0xFF, 0xFF, 1.5);

    private peakData? peaks;
    private double viewStart, viewEnd = 1, targetStart, targetEnd = 1;
    private double selStart, selEnd;
    private double playhead = -1;
    private dragHandle dragging = dragHandle.none;
    private double dragOffsetX;
    private double pointerX;
    private bool zoomedDrag;
    private bool animating;
    private long holdStartTicks;
    private double holdAnchorX;
    private bool holdFired;

    public event Action? selectionChanged;
    public event Action? dragStarted;

    public double selectionStart => selStart;
    public double selectionEnd => selEnd;

    public waveformView()
    {
        ClipToBounds = true;
        Unloaded += (_, _) => stopAnimation();
    }

    public void load(peakData data, double start, double end)
    {
        peaks = data;
        viewStart = targetStart = 0;
        viewEnd = targetEnd = Math.Max(data.duration, 0.001);
        selStart = Math.Clamp(start, 0, viewEnd);
        selEnd = Math.Clamp(end, selStart, viewEnd);
        if (selEnd - selStart < minSelectionSeconds) { selStart = 0; selEnd = viewEnd; }
        playhead = -1;
        InvalidateVisual();
    }

    public void clear()
    {
        peaks = null;
        dragging = dragHandle.none;
        zoomedDrag = false;
        stopAnimation();
        InvalidateVisual();
    }

    public void setPlayhead(double seconds)
    {
        playhead = seconds;
        InvalidateVisual();
    }

    private double duration => peaks?.duration ?? 1;
    private double viewSpan => Math.Max(viewEnd - viewStart, 1e-6);
    private double timeToX(double t) => (t - viewStart) / viewSpan * ActualWidth;
    private double xToTime(double x) => viewStart + x / Math.Max(ActualWidth, 1) * viewSpan;

    // ---------- input ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (peaks == null) return;

        double x = e.GetPosition(this).X;
        var hit = handleAt(x);
        if (hit != dragHandle.none)
        {
            dragging = hit;
            dragOffsetX = timeToX(hit == dragHandle.start ? selStart : selEnd) - x;
        }
        else
        {
            dragging = nearestHandle(x);
            dragOffsetX = 0;
            setHandle(dragging, xToTime(x));
        }

        pointerX = x;
        restartHold(x);
        CaptureMouse();
        dragStarted?.Invoke();
        startAnimation();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double x = e.GetPosition(this).X;

        if (dragging == dragHandle.none)
        {
            Cursor = peaks != null && handleAt(x) != dragHandle.none ? Cursors.SizeWE : null;
            return;
        }

        pointerX = x;
        if (!zoomedDrag && Math.Abs(x - holdAnchorX) > holdMoveTolerancePx) restartHold(x);
        setHandle(dragging, xToTime(Math.Clamp(x + dragOffsetX, 0, ActualWidth)));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        endDrag();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        endDrag();
    }

    private void restartHold(double x)
    {
        holdStartTicks = Stopwatch.GetTimestamp();
        holdAnchorX = x;
        holdFired = false;
    }

    private double holdElapsed => Stopwatch.GetElapsedTime(holdStartTicks).TotalSeconds;

    private double holdProgress =>
        dragging == dragHandle.none || zoomedDrag || holdFired ? 0 : Math.Min(1, holdElapsed / holdToZoomSeconds);

    private dragHandle handleAt(double x)
    {
        double startX = timeToX(selStart), endX = timeToX(selEnd);
        double ds = Math.Abs(x - startX), de = Math.Abs(x - endX);
        if (Math.Min(ds, de) > handleHitPx) return dragHandle.none;
        if (Math.Abs(ds - de) < 0.5) return x < startX ? dragHandle.start : dragHandle.end;
        return ds < de ? dragHandle.start : dragHandle.end;
    }

    private dragHandle nearestHandle(double x)
    {
        double startX = timeToX(selStart), endX = timeToX(selEnd);
        if (x <= startX) return dragHandle.start;
        if (x >= endX) return dragHandle.end;
        return x - startX <= endX - x ? dragHandle.start : dragHandle.end;
    }

    private void setHandle(dragHandle which, double t)
    {
        t = Math.Clamp(t, 0, duration);
        if (which == dragHandle.start) selStart = Math.Max(0, Math.Min(t, selEnd - minSelectionSeconds));
        else if (which == dragHandle.end) selEnd = Math.Min(duration, Math.Max(t, selStart + minSelectionSeconds));
        else return;

        selectionChanged?.Invoke();
        InvalidateVisual();
    }

    private void endDrag()
    {
        if (dragging == dragHandle.none) return;
        dragging = dragHandle.none;
        holdFired = false;
        InvalidateVisual();
        if (!zoomedDrag) return;

        zoomedDrag = false;
        targetStart = 0;
        targetEnd = duration;
        startAnimation();
    }

    // ---------- zoom + pan ----------

    // Zoom so the grabbed handle stays exactly where it is on screen.
    private void zoomAround(double t)
    {
        double zoomSpan = Math.Clamp(duration * zoomFraction, minZoomSeconds, maxZoomSeconds);
        if (zoomSpan >= duration * 0.8) return;

        double anchor = Math.Clamp(timeToX(t) / Math.Max(ActualWidth, 1), 0, 1);
        targetStart = Math.Clamp(t - anchor * zoomSpan, 0, duration - zoomSpan);
        targetEnd = targetStart + zoomSpan;
        zoomedDrag = true;
        startAnimation();
    }

    private void autoPan()
    {
        double direction = pointerX < panEdgePx ? -1 : pointerX > ActualWidth - panEdgePx ? 1 : 0;
        if (direction == 0) return;

        double shift = direction * (targetEnd - targetStart) * panSpeed;
        shift = Math.Clamp(shift, -targetStart, duration - targetEnd);
        if (shift == 0) return;

        targetStart += shift; targetEnd += shift;
        viewStart += shift; viewEnd += shift;
        setHandle(dragging, xToTime(Math.Clamp(pointerX + dragOffsetX, 0, ActualWidth)));
    }

    private void startAnimation()
    {
        if (animating) return;
        animating = true;
        CompositionTarget.Rendering += onFrame;
    }

    private void stopAnimation()
    {
        if (!animating) return;
        animating = false;
        CompositionTarget.Rendering -= onFrame;
    }

    private void onFrame(object? sender, EventArgs e)
    {
        if (peaks == null) { stopAnimation(); return; }
        if (dragging != dragHandle.none)
        {
            if (zoomedDrag) autoPan();
            else if (!holdFired && holdElapsed >= holdToZoomSeconds)
            {
                holdFired = true;
                zoomAround(dragging == dragHandle.start ? selStart : selEnd);
            }
        }

        viewStart += (targetStart - viewStart) * easing;
        viewEnd += (targetEnd - viewEnd) * easing;

        double tolerance = duration * 1e-5;
        if (Math.Abs(targetStart - viewStart) < tolerance && Math.Abs(targetEnd - viewEnd) < tolerance)
        {
            viewStart = targetStart;
            viewEnd = targetEnd;
            if (dragging == dragHandle.none) stopAnimation();
        }
        InvalidateVisual();
    }

    // ---------- drawing ----------

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (peaks == null || w < 2 || h < 2) return;

        double startX = timeToX(selStart), endX = timeToX(selEnd);
        double left = Math.Max(0, startX), right = Math.Min(w, endX);
        if (right > left) dc.DrawRectangle(selectionBrush, null, new Rect(left, 0, right - left, h));

        double mid = h / 2, maxHalf = h / 2 - 4;
        float norm = peaks.globalPeak > 0 ? peaks.globalPeak : 1;
        for (double x = 0; x < w; x += barStep)
        {
            double t0 = xToTime(x), t1 = xToTime(x + barStep);
            if (t0 >= duration) break;
            double level = Math.Pow(peaks.peakBetween(t0, t1) / norm, 0.7);
            double half = Math.Max(0.5, level * maxHalf);
            bool inside = t1 > selStart && t0 < selEnd;
            dc.DrawRectangle(inside ? barBrush : dimBarBrush, null, new Rect(x, mid - half, barWidth, half * 2));
        }

        double progress = holdProgress;
        drawHandle(dc, startX, w, h, dragging == dragHandle.start, progress, zoomedDrag);
        drawHandle(dc, endX, w, h, dragging == dragHandle.end, progress, zoomedDrag);

        if (playhead >= 0)
        {
            double px = timeToX(playhead);
            if (px >= -2 && px <= w + 2) dc.DrawRectangle(playheadBrush, null, new Rect(px - 1, 0, 2, h));
        }
    }

    private static void drawHandle(DrawingContext dc, double x, double w, double h, bool active, double progress, bool zoomed)
    {
        if (x < -12 || x > w + 12) return;
        dc.DrawRectangle(handleBrush, null, new Rect(x - 1, 0, 2, h));

        var knob = new Rect(x - 5, h / 2 - 13, 10, 26);
        if (active && zoomed)
        {
            dc.DrawRoundedRectangle(handleBrush, zoomedKnobPen, knob, 5, 5);
            return;
        }

        dc.DrawRoundedRectangle(knobBrush, null, knob, 5, 5);
        if (!active || progress <= 0) return;

        // Hold-to-zoom timer: the knob fills with the accent colour from the bottom up.
        dc.PushClip(new RectangleGeometry(knob, 5, 5));
        double fill = knob.Height * progress;
        dc.DrawRectangle(handleBrush, null, new Rect(knob.X, knob.Bottom - fill, knob.Width, fill));
        dc.Pop();
    }

    private static Brush frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static Pen frozenPen(byte a, byte r, byte g, byte b, double thickness)
    {
        var pen = new Pen(frozen(a, r, g, b), thickness);
        pen.Freeze();
        return pen;
    }
}
