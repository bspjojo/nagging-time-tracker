using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired().UseCollation("NOCASE");
        b.Property(x => x.Archived).HasColumnName("archived");
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_customers_name");
    }
}
