using NaggingTimeTracker.Core.Startup;

namespace NaggingTimeTracker.Tests.Core;

public sealed class InstanceNamesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_data_dir_gives_plain_names(string? dataDir)
    {
        Assert.Equal(
            new InstanceNames(@"Local\NaggingTimeTracker", @"Local\NaggingTimeTracker.Show"),
            InstanceNames.For(dataDir));
    }

    [Fact]
    public void Same_dir_in_different_case_and_trailing_slash_gives_same_names()
    {
        Assert.Equal(InstanceNames.For(@"C:\Temp\NTT"), InstanceNames.For(@"c:\temp\ntt\"));
    }

    [Fact]
    public void Different_dirs_give_different_names()
    {
        Assert.NotEqual(InstanceNames.For(@"C:\Temp\a").Mutex, InstanceNames.For(@"C:\Temp\b").Mutex);
    }

    [Fact]
    public void Suffix_is_eight_lowercase_hex_chars()
    {
        var names = InstanceNames.For(@"C:\Temp\ntt");

        Assert.Matches(@"^Local\\NaggingTimeTracker\.[0-9a-f]{8}$", names.Mutex);
        Assert.Equal(names.Mutex + ".Show", names.ShowEvent);
    }
}
