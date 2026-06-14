using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ChannelBindingConfiguration : IEntityTypeConfiguration<ChannelBinding>
{
    public void Configure(EntityTypeBuilder<ChannelBinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(b => b.Id);
        // One channel per feature per guild.
        builder.HasIndex(b => new
        {
            b.GuildId, b.Feature
        }).IsUnique();
    }
}
