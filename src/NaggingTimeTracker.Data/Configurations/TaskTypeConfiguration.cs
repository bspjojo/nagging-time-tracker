using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Configurations;

public sealed class TaskTypeConfiguration : IEntityTypeConfiguration<TaskType>
{
    /// <summary>2026-01-01T00:00:00Z — deterministic CreatedUtc for the seeded task types.</summary>
    public const long SeedCreatedUtcMs = 1767225600000;

    public static readonly string[] SeedNames = ["Admin", "Training", "Dev Work", "Meetings", "Support"];

    public void Configure(EntityTypeBuilder<TaskType> b)
    {
        b.ToTable("task_types");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired().UseCollation("NOCASE");
        b.Property(x => x.Archived).HasColumnName("archived");
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_task_types_name");

        var created = DateTimeOffset.FromUnixTimeMilliseconds(SeedCreatedUtcMs);
        b.HasData(SeedNames.Select((name, i) => new TaskType
        {
            Id = i + 1,
            Name = name,
            Archived = false,
            CreatedUtc = created,
        }));
    }
}
