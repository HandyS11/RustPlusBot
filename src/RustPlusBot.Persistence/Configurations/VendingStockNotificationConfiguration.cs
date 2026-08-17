using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class VendingStockNotificationConfiguration : IEntityTypeConfiguration<VendingStockNotification>
{
    public void Configure(EntityTypeBuilder<VendingStockNotification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SoldOutSignature).IsRequired().HasMaxLength(512);
        builder.HasIndex(s => new { s.GuildId, s.ServerId, s.MachineId }).IsUnique();

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
