using System.Runtime.InteropServices;

namespace NaggingTimeTracker.App.Idle;

[StructLayout(LayoutKind.Sequential)]
internal struct LASTINPUTINFO
{
    public uint cbSize;
    public uint dwTime;
}

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Time since the last keyboard or mouse input in this session; null if the call fails.</summary>
    internal static TimeSpan? SinceLastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        // Unsigned subtraction survives the 49.7-day wrap of the 32-bit tick count.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount64 - info.dwTime));
    }
}
