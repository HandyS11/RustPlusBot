using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ConnectionStateConfiguration : IEntityTypeConfiguration<ConnectionState>
{
    public void Configure(EntityTypeBuilder<ConnectionState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.RustServerId);

        // Removing a RustServer cascades to its single connection-state row so no orphaned status lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ConnectionState>(s => s.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
