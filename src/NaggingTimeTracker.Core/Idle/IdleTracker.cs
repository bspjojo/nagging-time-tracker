using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Core.Idle;

/// <summary>A period the user was away (or the app wasn't running).</summary>
public readonly record struct IdleSpan(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

public readonly record struct IdleOptions(bool Enabled, TimeSpan Threshold)
{
    public static IdleOptions From(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.IdleEnabled, TimeSpan.FromMinutes(s.IdleThresholdMin));
    }
}

/// <summary>
/// The idle state machine, with no timers or OS calls of its own: <c>IdleMonitor</c> feeds it the
/// time since the last input and the session/power events. Lock and suspend count as idle at once;
/// a return only produces a span when the absence reached the threshold.
/// </summary>
public sealed class IdleTracker
{
    /// <summary>Input this close after the idle start is rounding noise, not a return.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(1);

    private bool _locked;
    private bool _suspended;

    public DateTimeOffset? IdleStartUtc { get; private set; }

    public bool IsUserIdle => IdleStartUtc is not null;

    public IdleSpan? Poll(DateTimeOffset nowUtc, TimeSpan sinceLastInput, IdleOptions o)
    {
        if (!o.Enabled)
        {
            Reset();
            return null;
        }

        // Input on the lock screen (or while waking) isn't a return.
        if (_locked || _suspended)
        {
            return null;
        }

        var lastInput = nowUtc - sinceLastInput;
        if (IdleStartUtc is null)
        {
            if (sinceLastInput >= o.Threshold)
            {
                IdleStartUtc = lastInput;
            }

            return null;
        }

        return lastInput > IdleStartUtc.Value + Margin ? End(lastInput, o) : null;
    }

    public void Lock(DateTimeOffset nowUtc, TimeSpan sinceLastInput, IdleOptions o)
    {
        if (!o.Enabled)
        {
            return;
        }

        _locked = true;
        IdleStartUtc ??= nowUtc - sinceLastInput;
    }

    public IdleSpan? Unlock(DateTimeOffset nowUtc, IdleOptions o)
    {
        _locked = false;
        if (!o.Enabled || _suspended || IdleStartUtc is null)
        {
            return null;
        }

        return End(nowUtc, o);
    }

    public void Suspend(DateTimeOffset nowUtc, TimeSpan sinceLastInput, IdleOptions o)
    {
        if (!o.Enabled)
        {
            return;
        }

        _suspended = true;
        IdleStartUtc ??= nowUtc - sinceLastInput;
    }

    /// <summary>Waking alone doesn't end the idle period; the next real input or an unlock does.</summary>
    public void Resume() => _suspended = false;

    public void Reset()
    {
        IdleStartUtc = null;
        _locked = false;
        _suspended = false;
    }

    private IdleSpan? End(DateTimeOffset endUtc, IdleOptions o)
    {
        var span = new IdleSpan(IdleStartUtc!.Value, endUtc);
        IdleStartUtc = null;
        return span.Duration >= o.Threshold ? span : null;
    }
}
