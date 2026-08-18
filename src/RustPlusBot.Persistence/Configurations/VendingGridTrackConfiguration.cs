using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class VendingGridTrackConfiguration : IEntityTypeConfiguration<VendingGridTrack>
{
    public void Configure(EntityTypeBuilder<VendingGridTrack> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Grid).IsRequired().HasMaxLength(8);
        builder.HasIndex(g => new
        {
            g.GuildId, g.ServerId, g.Grid
        }).IsUnique();

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(g => g.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
