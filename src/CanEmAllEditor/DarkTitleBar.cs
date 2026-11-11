using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CanEmAllEditor;

/// <summary>Makes the Windows title bar and frame dark (Windows 10 1809+ / Windows 11) so no white border shows.</summary>
static class DarkTitleBar
{
    const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWA_CAPTION_COLOR = 35;
    const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>COLORREF is 0x00BBGGRR.</summary>
    static int Rgb(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));

            // Windows 11 only: exact colors matching the sidebar (#161922) and the card borders (#2A3042)
            int caption = Rgb(0x16, 0x19, 0x22), border = Rgb(0x2A, 0x30, 0x42), text = Rgb(0xE7, 0xE9, 0xF0);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }
        catch (Exception) { /* older Windows: keep the default frame */ }
    }
}
