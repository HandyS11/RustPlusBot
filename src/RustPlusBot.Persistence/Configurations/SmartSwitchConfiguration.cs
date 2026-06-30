using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Switches;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class SmartSwitchConfiguration : IEntityTypeConfiguration<SmartSwitch>
{
    public void Configure(EntityTypeBuilder<SmartSwitch> builder)
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

        // Removing a RustServer cascades to its switches so no orphaned rows linger.
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
