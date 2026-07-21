using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Clans;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ClanPlayerNameConfiguration : IEntityTypeConfiguration<ClanPlayerName>
{
    public void Configure(EntityTypeBuilder<ClanPlayerName> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(n => new { n.ServerId, n.SteamId });
        builder.Property(n => n.Name).HasMaxLength(64);

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(n => n.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
