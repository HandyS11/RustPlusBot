using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Commands;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ServerCommandSettingsConfiguration : IEntityTypeConfiguration<ServerCommandSettings>
{
    public void Configure(EntityTypeBuilder<ServerCommandSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.ServerId);
        builder.Property(s => s.Prefix).HasMaxLength(8);

        // Removing a RustServer cascades to its single command-settings row so no orphaned config lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ServerCommandSettings>(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
