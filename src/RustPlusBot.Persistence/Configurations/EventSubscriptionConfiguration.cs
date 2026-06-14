using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Events;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class EventSubscriptionConfiguration : IEntityTypeConfiguration<EventSubscription>
{
    public void Configure(EntityTypeBuilder<EventSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new
        {
            e.GuildId, e.RustServerId
        });
        builder.Property(e => e.EventKey).IsRequired().HasMaxLength(64);
    }
}
