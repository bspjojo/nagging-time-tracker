using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Configurations;

public sealed class ReminderConfiguration : IEntityTypeConfiguration<ScheduledReminder>
{
    private static readonly ValueConverter<TimeOnly, string> TimeLocalConverter = new(
        t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
        s => TimeOnly.ParseExact(s, "HH:mm", CultureInfo.InvariantCulture));

    public void Configure(EntityTypeBuilder<ScheduledReminder> b)
    {
        b.ToTable("reminders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.Message).HasColumnName("message").IsRequired();
        b.Property(x => x.DaysMask).HasColumnName("days_mask");
        b.Property(x => x.TimeLocal).HasColumnName("time_local").HasConversion(TimeLocalConverter).IsRequired();
        b.Property(x => x.Enabled).HasColumnName("enabled");
        b.Property(x => x.LastFiredUtc).HasColumnName("last_fired_utc");
    }
}
