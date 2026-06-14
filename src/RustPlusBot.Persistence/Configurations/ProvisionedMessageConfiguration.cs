using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ProvisionedMessageConfiguration : IEntityTypeConfiguration<ProvisionedMessage>
{
    public void Configure(EntityTypeBuilder<ProvisionedMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MessageKey).IsRequired().HasMaxLength(64);
        builder.HasIndex(m => new
        {
            m.GuildId, m.RustServerId, m.MessageKey
        }).IsUnique();
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(m => m.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
