using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class PlayerCredentialConfiguration : IEntityTypeConfiguration<PlayerCredential>
{
    public void Configure(EntityTypeBuilder<PlayerCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new
        {
            c.GuildId, c.RustServerId
        });
        builder.Property(c => c.ProtectedPlayerToken).IsRequired();
        builder.Property(c => c.ProtectedFcmCredentials).IsRequired();
    }
}
