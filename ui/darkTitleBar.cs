using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace boardzz.ui;

// Asks Windows (10 20H1+ / 11) to draw the window's title bar in dark mode.
internal static class darkTitleBar
{
    private const int useImmersiveDarkMode = 20;

    public static void apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = 1;
        DwmSetWindowAttribute(handle, useImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
