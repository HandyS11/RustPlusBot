using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class SmartAlarmConfiguration : IEntityTypeConfiguration<SmartAlarm>
{
    public void Configure(EntityTypeBuilder<SmartAlarm> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(a => new
        {
            a.GuildId, a.ServerId, a.EntityId
        }).IsUnique();

        builder.Property(a => a.Reachability)
            .HasConversion<int>()
            .HasDefaultValue(DeviceReachability.Reachable);

        // Removing a RustServer cascades to its alarms so no orphaned rows linger.
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(a => a.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
