using NaggingTimeTracker.Core.Startup;

namespace NaggingTimeTracker.Tests.Core;

public sealed class RunAtLoginTests
{
    private const string Exe = @"C:\Program Files\NaggingTimeTracker\NaggingTimeTracker.exe";
    private readonly FakeRunKey _key = new();
    private readonly RunAtLogin _run;

    public RunAtLoginTests() => _run = new RunAtLogin(_key);

    private sealed class FakeRunKey : IRunKeyStore
    {
        public string? Value { get; set; }

        public int Writes { get; private set; }

        public string? Get() => Value;

        public void Set(string value)
        {
            Value = value;
            Writes++;
        }

        public void Remove() => Value = null;
    }

    [Fact]
    public void Enable_writes_the_quoted_path_and_disable_removes_it()
    {
        Assert.False(_run.IsEnabled);

        _run.Enable(Exe);
        Assert.Equal($"\"{Exe}\"", _key.Value);
        Assert.True(_run.IsEnabled);

        _run.Disable();
        Assert.Null(_key.Value);
        Assert.False(_run.IsEnabled);
    }

    [Fact]
    public void Heal_leaves_an_absent_value_absent()
    {
        Assert.False(_run.HealIfMoved(Exe));
        Assert.Null(_key.Value);
        Assert.Equal(0, _key.Writes);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\NaggingTimeTracker\\NaggingTimeTracker.exe\"")]
    [InlineData("\"c:\\program files\\naggingtimetracker\\NAGGINGTIMETRACKER.EXE\"")]
    [InlineData("C:\\Program Files\\NaggingTimeTracker\\NaggingTimeTracker.exe")]
    public void Heal_leaves_the_same_path_alone(string stored)
    {
        _key.Value = stored;

        Assert.False(_run.HealIfMoved(Exe));
        Assert.Equal(stored, _key.Value);
        Assert.Equal(0, _key.Writes);
    }

    [Theory]
    [InlineData("\"C:\\old\\NaggingTimeTracker.exe\"")]
    [InlineData("\"\"")]
    [InlineData("\"C:\\bad<path>|.exe\"")]
    public void Heal_rewrites_a_moved_or_unusable_value(string stored)
    {
        _key.Value = stored;

        Assert.True(_run.HealIfMoved(Exe));
        Assert.Equal($"\"{Exe}\"", _key.Value);
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\"", "C:\\a b\\x.exe")]
    [InlineData("  C:\\x.exe ", "C:\\x.exe")]
    [InlineData("\" C:\\x.exe \"", "C:\\x.exe")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\"\"", null)]
    public void ParsePath_strips_quotes_and_whitespace(string value, string? expected) =>
        Assert.Equal(expected, RunAtLogin.ParsePath(value));
}
