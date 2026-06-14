using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ProvisionedChannelConfiguration : IEntityTypeConfiguration<ProvisionedChannel>
{
    public void Configure(EntityTypeBuilder<ProvisionedChannel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ChannelKey).IsRequired().HasMaxLength(64);
        builder.HasIndex(c => new
        {
            c.GuildId, c.RustServerId, c.ChannelKey
        }).IsUnique();
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(c => c.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
