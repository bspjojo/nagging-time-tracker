namespace NaggingTimeTracker.App.ViewModels;

/// <summary>An item of a filter dropdown; a null id means "all".</summary>
public sealed record FilterOption(long? Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>An item of the date-preset dropdown.</summary>
public sealed record PresetOption(NaggingTimeTracker.Core.Time.DatePreset Preset, string Label)
{
    public override string ToString() => Label;
}
