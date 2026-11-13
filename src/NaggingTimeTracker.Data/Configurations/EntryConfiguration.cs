using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Configurations;

public sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> b)
    {
        b.ToTable("entries", t => t.HasCheckConstraint(
            "ck_entries_stop_after_start", "stop_utc IS NULL OR stop_utc > start_utc"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CustomerId).HasColumnName("customer_id");
        b.Property(x => x.TaskTypeId).HasColumnName("task_type_id");
        b.Property(x => x.StartUtc).HasColumnName("start_utc");
        b.Property(x => x.StopUtc).HasColumnName("stop_utc");
        b.Property(x => x.ExternalLink).HasColumnName("external_link");
        b.Property(x => x.Notes).HasColumnName("notes").IsRequired();
        b.Property(x => x.ContinuedFromId).HasColumnName("continued_from_id");
        b.Property(x => x.OverlapReason).HasColumnName("overlap_reason");
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");

        b.HasIndex(x => x.StartUtc).HasDatabaseName("ix_entries_start");
        b.HasIndex(x => x.StopUtc).HasFilter("stop_utc IS NULL").HasDatabaseName("ix_entries_running");

        // Customers and task types are archived, never deleted.
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaskType>().WithMany().HasForeignKey(x => x.TaskTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Entry>().WithMany().HasForeignKey(x => x.ContinuedFromId).OnDelete(DeleteBehavior.SetNull);
    }
}
