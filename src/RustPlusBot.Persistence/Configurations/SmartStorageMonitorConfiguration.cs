using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.StorageMonitors;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class SmartStorageMonitorConfiguration : IEntityTypeConfiguration<SmartStorageMonitor>
{
    public void Configure(EntityTypeBuilder<SmartStorageMonitor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(s => new
        {
            s.GuildId, s.ServerId, s.EntityId
        }).IsUnique();

        builder.Property(s => s.Reachability)
            .HasConversion<int>()
            .HasDefaultValue(DeviceReachability.Reachable);

        // Removing a RustServer cascades to its storage monitors so no orphaned rows linger.
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
