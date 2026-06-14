using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class RustServerConfiguration : IEntityTypeConfiguration<RustServer>
{
    public void Configure(EntityTypeBuilder<RustServer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(128);
        builder.Property(s => s.Ip).IsRequired().HasMaxLength(255);
        builder.HasIndex(s => s.GuildId);
    }
}
