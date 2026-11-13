using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NaggingTimeTracker.Data.Entities;

namespace NaggingTimeTracker.Data.Configurations;

public sealed class SettingConfiguration : IEntityTypeConfiguration<SettingRow>
{
    public void Configure(EntityTypeBuilder<SettingRow> b)
    {
        b.ToTable("settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasColumnName("key");
        b.Property(x => x.Value).HasColumnName("value").IsRequired();
    }
}
