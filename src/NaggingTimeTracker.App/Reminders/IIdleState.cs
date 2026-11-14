namespace NaggingTimeTracker.App.Reminders;

/// <summary>Whether the user has been away long enough that nagging is pointless (Phase 6 implements it).</summary>
public interface IIdleState
{
    bool IsUserIdle { get; }
}

/// <summary>Until idle detection exists (Phase 6), the user is never idle.</summary>
public sealed class NeverIdle : IIdleState
{
    public bool IsUserIdle => false;
}
