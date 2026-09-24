using Bit.Infrastructure.EntityFramework.AdminConsole.Models.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bit.Infrastructure.EntityFramework.AdminConsole.Configurations;

public class ProviderApiKeyEntityTypeConfiguration : IEntityTypeConfiguration<ProviderApiKey>
{
    public void Configure(EntityTypeBuilder<ProviderApiKey> builder)
    {
        builder
            .Property(e => e.Id)
            .ValueGeneratedNever();

        builder
            .HasOne<Provider>()
            .WithMany()
            .HasForeignKey(e => e.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(e => new { e.ProviderId, e.Type })
            .IsUnique()
            .IsClustered(false);

        builder.ToTable(nameof(ProviderApiKey));
    }
}
