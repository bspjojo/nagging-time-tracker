using System.Runtime.InteropServices;

namespace NaggingTimeTracker.App.Tray;

internal static class NativeMethods
{
    /// <summary>Width of a small icon (the tray size) in physical pixels at the system DPI.</summary>
    public const int SM_CXSMICON = 49;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);
}
