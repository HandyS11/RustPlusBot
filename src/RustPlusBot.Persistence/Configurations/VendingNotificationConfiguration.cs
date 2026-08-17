using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class VendingNotificationConfiguration : IEntityTypeConfiguration<VendingNotification>
{
    public void Configure(EntityTypeBuilder<VendingNotification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(n => n.Id);
        builder.HasIndex(x => new
        {
            x.GuildId, x.ServerId, x.ItemId, x.ItemIsBlueprint, x.CurrencyId, x.CurrencyIsBlueprint
        }).IsUnique();

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(n => n.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
