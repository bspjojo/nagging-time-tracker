using NaggingTimeTracker.Core.Tray;

namespace NaggingTimeTracker.Tests.Core;

public sealed class TrayIconSelectorTests
{
    [Theory]
    [InlineData(false, 0, "idle.ico")]
    [InlineData(false, 1, "tracking-1.ico")]
    [InlineData(false, 2, "tracking-2.ico")]
    [InlineData(false, 9, "tracking-9.ico")]
    [InlineData(false, 10, "tracking-9plus.ico")]
    [InlineData(false, 250, "tracking-9plus.ico")]
    [InlineData(true, 0, "attention-0.ico")]
    [InlineData(true, 1, "attention-1.ico")]
    [InlineData(true, 2, "attention-2.ico")]
    [InlineData(true, 9, "attention-9.ico")]
    [InlineData(true, 10, "attention-9plus.ico")]
    public void Selects_the_right_file(bool hasAttention, int running, string expected)
    {
        Assert.Equal(expected, TrayIconSelector.Select(hasAttention, running));
    }

    [Fact]
    public void Negative_count_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrayIconSelector.Select(false, -1));
    }

    [Fact]
    public void AllFileNames_has_22_distinct_names()
    {
        Assert.Equal(22, TrayIconSelector.AllFileNames.Distinct().Count());
        Assert.Equal(22, TrayIconSelector.AllFileNames.Count);
    }

    [Fact]
    public void All_icon_files_exist()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "tray");
        var missing = TrayIconSelector.AllFileNames.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();

        Assert.Empty(missing);
        Assert.Equal(22, Directory.GetFiles(dir, "*.ico").Length);
    }
}
