using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Entities;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class PairedEntityConfiguration : IEntityTypeConfiguration<PairedEntity>
{
    public void Configure(EntityTypeBuilder<PairedEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.GuildId, e.RustServerId });
        builder.Property(e => e.Name).IsRequired().HasMaxLength(128);
    }
}
