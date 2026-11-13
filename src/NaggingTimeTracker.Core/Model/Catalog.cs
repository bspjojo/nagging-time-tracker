namespace NaggingTimeTracker.Core.Model;

/// <summary>Common shape of customers and task types (archived, never deleted).</summary>
public interface ICatalogItem
{
    long Id { get; }
    string Name { get; }
    bool Archived { get; }
}

public sealed record Customer : ICatalogItem
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public bool Archived { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record TaskType : ICatalogItem
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public bool Archived { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}
