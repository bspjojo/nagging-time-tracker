using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace NaggingTimeTracker.Data.Converters;

/// <summary>
/// Stores every <see cref="DateTimeOffset"/> as INTEGER Unix epoch milliseconds (UTC), so
/// comparisons and ORDER BY translate to plain integer SQL. Reads come back with offset zero.
/// </summary>
public sealed class EpochMsConverter() : ValueConverter<DateTimeOffset, long>(
    v => v.ToUnixTimeMilliseconds(),
    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
