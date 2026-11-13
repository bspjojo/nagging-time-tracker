using NaggingTimeTracker.Core.Abstractions;

namespace NaggingTimeTracker.Tests.Data;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ISettingsStore _store;

    public SettingsStoreTests() => _store = _db.Get<ISettingsStore>();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Missing_key_returns_null()
    {
        Assert.Null(_store.Get("work_start"));
    }

    [Fact]
    public void Set_then_Get_and_overwrite_keeps_one_row()
    {
        _store.Set("work_start", "09:00");
        Assert.Equal("09:00", _store.Get("work_start"));

        _store.Set("work_start", "08:30");
        Assert.Equal("08:30", _store.Get("work_start"));
        Assert.Equal(1L, _db.Scalar("SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void Remove_unsets_and_is_a_no_op_when_missing()
    {
        _store.Set("heartbeat_utc", "1767225600000");

        _store.Remove("heartbeat_utc");
        Assert.Null(_store.Get("heartbeat_utc"));

        _store.Remove("heartbeat_utc");
        _store.Remove("never_set");
    }

    [Fact]
    public void Values_with_quotes_are_stored_verbatim()
    {
        const string value = "it's \"quoted\"; DROP TABLE settings;";
        _store.Set("k", value);

        Assert.Equal(value, _store.Get("k"));
    }
}
