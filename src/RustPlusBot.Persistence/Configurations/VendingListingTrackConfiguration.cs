using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class VendingListingTrackConfiguration : IEntityTypeConfiguration<VendingListingTrack>
{
    public void Configure(EntityTypeBuilder<VendingListingTrack> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(l => l.Id);
        builder.HasIndex(x => new
        {
            x.GuildId,
            x.ServerId,
            x.ItemId,
            x.ItemIsBlueprint,
            x.CurrencyId,
            x.CurrencyIsBlueprint
        }).IsUnique();

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(l => l.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
