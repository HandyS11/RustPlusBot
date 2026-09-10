using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class GuildSettingsConfiguration : IEntityTypeConfiguration<GuildSettings>
{
    public void Configure(EntityTypeBuilder<GuildSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Persistord's SnowflakeKeyConvention already marks every ulong key caller-supplied.
        builder.HasKey(s => s.GuildId);
        builder.Property(s => s.Culture).IsRequired().HasMaxLength(16);
    }
}
