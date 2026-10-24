using System.Windows;
using System.Windows.Media;

namespace boardzz.ui;

// Scrolling bar waveform: push one level (0..1) per frame, newest on the right.
internal sealed class liveWave : FrameworkElement
{
    private const int barCount = 56;
    private static readonly Brush barBrush = makeBrush();

    private readonly double[] levels = new double[barCount];
    private int head;
    private int quietFrames = barCount;

    public void push(double level)
    {
        levels[head] = level;
        head = (head + 1) % barCount;

        // Keep redrawing until the last loud bar has scrolled off, then go idle.
        quietFrames = level > 0 ? 0 : quietFrames + 1;
        if (quietFrames <= barCount) InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        double slot = width / barCount;
        double barWidth = Math.Max(1, slot * 0.6);
        double middle = height / 2;

        for (int i = 0; i < barCount; i++)
        {
            double level = levels[(head + i) % barCount];
            double barHeight = Math.Max(2, level * height);
            var rect = new Rect(i * slot + (slot - barWidth) / 2, middle - barHeight / 2, barWidth, barHeight);
            dc.DrawRoundedRectangle(barBrush, null, rect, barWidth / 2, barWidth / 2);
        }
    }

    private static Brush makeBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
        brush.Freeze();
        return brush;
    }
}
