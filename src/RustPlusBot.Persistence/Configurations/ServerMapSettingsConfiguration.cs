using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Map;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ServerMapSettingsConfiguration : IEntityTypeConfiguration<ServerMapSettings>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServerMapSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.ServerId);

        // Removing a RustServer cascades to its single map-settings row so no orphaned config lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ServerMapSettings>(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
