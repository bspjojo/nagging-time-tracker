namespace NaggingTimeTracker.App.Reminders;

/// <summary>
/// Whether nags should wait: a full-screen app, a presentation, or another "busy" state.
/// QUNS_APP is deliberately excluded because it also reports ordinary Store apps such as Settings.
/// </summary>
public sealed class PresentationState
{
    public bool IsBusy()
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) != 0)
        {
            return false; // a failed query must never block nags forever
        }

        return state is NativeMethods.QUNS_BUSY
            or NativeMethods.QUNS_RUNNING_D3D_FULL_SCREEN
            or NativeMethods.QUNS_PRESENTATION_MODE;
    }
}
