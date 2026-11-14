using System.Runtime.InteropServices;

namespace NaggingTimeTracker.App.Reminders;

internal static class NativeMethods
{
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);
}
