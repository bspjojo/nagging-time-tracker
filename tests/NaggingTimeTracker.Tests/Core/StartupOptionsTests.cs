using NaggingTimeTracker.Core.Startup;

namespace NaggingTimeTracker.Tests.Core;

public sealed class StartupOptionsTests
{
    [Fact]
    public void No_args_gives_defaults()
    {
        Assert.Equal(new StartupOptions(null, ShowTarget.None, false), StartupOptions.Parse([]));
    }

    [Fact]
    public void Parses_all_switches()
    {
        var options = StartupOptions.Parse(["--data-dir", @"C:\tmp\ntt", "--show", "quick-panel", "--no-background"]);

        Assert.Equal(new StartupOptions(@"C:\tmp\ntt", ShowTarget.QuickPanel, true), options);
    }

    [Fact]
    public void Show_value_is_case_insensitive()
    {
        Assert.Equal(ShowTarget.QuickPanel, StartupOptions.Parse(["--show", "QUICK-PANEL"]).Show);
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("--data-dir")]
    [InlineData("--data-dir", "--no-background")]
    [InlineData("--show", "entries")]
    [InlineData("--show", "")]
    [InlineData("--no-background", "--no-background")]
    [InlineData("--show", "quick-panel", "--show", "quick-panel")]
    [InlineData("--DATA-DIR", "x")]
    public void Invalid_args_throw(params string[] args)
    {
        Assert.Throws<StartupOptionsException>(() => StartupOptions.Parse(args));
    }
}
