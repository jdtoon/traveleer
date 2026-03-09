using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using saas.Data.Tenant;
using saas.Modules.Settings.Entities;

namespace saas.Modules.Settings.Data;

public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>, ITenantEntityConfiguration
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Symbol).HasMaxLength(10);
        builder.Property(x => x.ExchangeRate).HasPrecision(18, 6);
        builder.Property(x => x.DefaultMarkup).HasPrecision(6, 2);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.IsBaseCurrency);
    }
}
