using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Clans;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ClanStateConfiguration : IEntityTypeConfiguration<ClanState>
{
    public void Configure(EntityTypeBuilder<ClanState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.ServerId);
        builder.Property(s => s.Name).HasMaxLength(128);
        builder.Property(s => s.Motd).HasMaxLength(1024);
        builder.Property(s => s.LogoHash).HasMaxLength(64);

        // Removing a RustServer cascades to its clan state so no orphaned snapshot lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ClanState>(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
