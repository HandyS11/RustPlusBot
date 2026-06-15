using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class FcmRegistrationConfiguration : IEntityTypeConfiguration<FcmRegistration>
{
    public void Configure(EntityTypeBuilder<FcmRegistration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new
        {
            r.GuildId, r.OwnerUserId
        }).IsUnique();
        builder.Property(r => r.ProtectedFcmCredentials).IsRequired();
    }
}
