namespace NaggingTimeTracker.App.Reminders;

/// <summary>Idle state for the reminder engine (implemented by IdleMonitor).</summary>
public interface IIdleState
{
    /// <summary>The user has been away long enough that nagging is pointless.</summary>
    bool IsUserIdle { get; }

    /// <summary>The idle prompt is open; counts as "prompt open" for the engine.</summary>
    bool IsPromptOpen { get; }
}

/// <summary>Used under --no-background, where idle detection doesn't run.</summary>
public sealed class NeverIdle : IIdleState
{
    public bool IsUserIdle => false;

    public bool IsPromptOpen => false;
}
