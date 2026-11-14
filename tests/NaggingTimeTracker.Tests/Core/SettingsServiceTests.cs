using System.Globalization;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Tests.Core;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly ISettingsStore _store;
    private readonly SettingsService _service;
    private int _changed;

    public SettingsServiceTests()
    {
        _store = _db.Get<ISettingsStore>();
        _service = new SettingsService(_store);
        _service.SettingsChanged += (_, _) => _changed++;
    }

    public void Dispose() => _db.Dispose();

    private static AppSettings Custom => new()
    {
        WorkDaysMask = 0b0111110 | 1,
        WorkStart = new TimeOnly(22, 0),
        WorkEnd = new TimeOnly(6, 30),
        NagNotTrackingEnabled = false,
        NagNotTrackingIntervalMin = 5,
        CheckinEnabled = false,
        CheckinIntervalMin = 90,
        IdleEnabled = false,
        IdleThresholdMin = 3,
    };

    [Fact]
    public void Empty_store_gives_defaults()
    {
        var s = _service.Load();

        Assert.Equal(new AppSettings(), s);
        Assert.Equal(62, s.WorkDaysMask);
        Assert.Equal(new TimeOnly(9, 0), s.WorkStart);
        Assert.Equal(new TimeOnly(17, 0), s.WorkEnd);
        Assert.Equal(15, s.NagNotTrackingIntervalMin);
        Assert.Equal(60, s.CheckinIntervalMin);
        Assert.Equal(10, s.IdleThresholdMin);
        Assert.True(s.NagNotTrackingEnabled && s.CheckinEnabled && s.IdleEnabled);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        _service.Save(Custom);

        Assert.Equal(Custom, _service.Load());
    }

    [Fact]
    public void Unparsable_values_fall_back_to_defaults()
    {
        _store.Set(SettingKeys.WorkDaysMask, "abc");
        _store.Set(SettingKeys.WorkStart, "25:99");
        _store.Set(SettingKeys.CheckinEnabled, "yes");
        _store.Set(SettingKeys.IdleThresholdMin, "1.5");

        Assert.Equal(new AppSettings(), _service.Load());
    }

    [Fact]
    public void Values_are_stored_in_invariant_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            _service.Save(new AppSettings());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("62", _store.Get(SettingKeys.WorkDaysMask));
        Assert.Equal("09:00", _store.Get(SettingKeys.WorkStart));
        Assert.Equal("17:00", _store.Get(SettingKeys.WorkEnd));
        Assert.Equal("true", _store.Get(SettingKeys.CheckinEnabled));
        Assert.Equal("60", _store.Get(SettingKeys.CheckinIntervalMin));
    }

    [Theory]
    [InlineData(-1, 15, 60, 10)]
    [InlineData(128, 15, 60, 10)]
    [InlineData(62, 0, 60, 10)]
    [InlineData(62, 15, 0, 10)]
    [InlineData(62, 15, 60, 0)]
    public void Out_of_range_values_throw_and_write_nothing(int mask, int nag, int checkin, int idle)
    {
        var bad = new AppSettings
        {
            WorkDaysMask = mask,
            NagNotTrackingIntervalMin = nag,
            CheckinIntervalMin = checkin,
            IdleThresholdMin = idle,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Save(bad));
        Assert.Null(_store.Get(SettingKeys.WorkDaysMask));
        Assert.Equal(0, _changed);
    }

    [Fact]
    public void SettingsChanged_is_raised_by_Save_only()
    {
        _service.SetInstant(SettingKeys.HeartbeatUtc, TestDb.T0);
        _service.Load();
        Assert.Equal(0, _changed);

        _service.Save(Custom);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public void Instants_round_trip_as_epoch_ms_and_null_removes()
    {
        var at = TestDb.T0.AddMilliseconds(123);

        _service.SetInstant(SettingKeys.RemindersPausedUntilUtc, at);
        Assert.Equal(at, _service.GetInstant(SettingKeys.RemindersPausedUntilUtc));
        Assert.Equal(at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            _store.Get(SettingKeys.RemindersPausedUntilUtc));

        _service.SetInstant(SettingKeys.RemindersPausedUntilUtc, null);
        Assert.Null(_store.Get(SettingKeys.RemindersPausedUntilUtc));
        Assert.Null(_service.GetInstant(SettingKeys.RemindersPausedUntilUtc));
    }

    [Fact]
    public void Unparsable_instant_reads_as_null()
    {
        _store.Set(SettingKeys.HeartbeatUtc, "yesterday");

        Assert.Null(_service.GetInstant(SettingKeys.HeartbeatUtc));
    }

    [Fact]
    public void Load_reads_the_store_once()
    {
        var store = new CountingStore();
        var service = new SettingsService(store);

        var first = service.Load();
        var reads = store.Gets;
        var second = service.Load();

        Assert.Equal(9, reads);
        Assert.Equal(reads, store.Gets);
        Assert.Same(first, second);
    }

    [Fact]
    public void Save_then_Load_returns_the_saved_values_without_rereading()
    {
        var store = new CountingStore();
        var service = new SettingsService(store);
        Assert.Equal(new AppSettings(), service.Load());
        var reads = store.Gets;

        service.Save(Custom);

        Assert.Equal(Custom, service.Load());
        Assert.Equal(reads, store.Gets);
    }

    [Fact]
    public void Cached_times_match_what_the_store_holds()
    {
        var store = new CountingStore();
        var service = new SettingsService(store);

        service.Save(Custom with { WorkStart = new TimeOnly(8, 15, 42), WorkEnd = new TimeOnly(16, 45, 0, 500) });

        Assert.Equal(new SettingsService(store).Load(), service.Load());
        Assert.Equal(new TimeOnly(8, 15), service.Load().WorkStart);
    }

    [Fact]
    public void A_failed_Save_makes_the_next_Load_reread_the_store()
    {
        var store = new CountingStore();
        var service = new SettingsService(store);
        service.Load();
        store.FailOn = SettingKeys.CheckinEnabled;

        Assert.Throws<InvalidOperationException>(() => service.Save(Custom));
        store.FailOn = null;
        var reads = store.Gets;

        var loaded = service.Load();

        Assert.Equal(reads + 9, store.Gets);
        Assert.Equal(new SettingsService(store).Load(), loaded);
    }

    [Fact]
    public void GetInstant_reads_the_store_every_time()
    {
        var store = new CountingStore();
        var service = new SettingsService(store);
        var at = TestDb.T0;
        service.SetInstant(SettingKeys.RemindersPausedUntilUtc, at);

        Assert.Equal(at, service.GetInstant(SettingKeys.RemindersPausedUntilUtc));
        store.Set(SettingKeys.RemindersPausedUntilUtc,
            at.AddHours(1).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        Assert.Equal(at.AddHours(1), service.GetInstant(SettingKeys.RemindersPausedUntilUtc));

        Assert.Equal(2, store.Gets);
    }

    /// <summary>An in-memory store that counts reads and can fail a chosen write.</summary>
    private sealed class CountingStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];

        public int Gets { get; private set; }

        public string? FailOn { get; set; }

        public string? Get(string key)
        {
            Gets++;
            return _values.GetValueOrDefault(key);
        }

        public void Set(string key, string value)
        {
            if (key == FailOn)
            {
                throw new InvalidOperationException("write failed");
            }

            _values[key] = value;
        }

        public void Remove(string key) => _values.Remove(key);
    }
}
