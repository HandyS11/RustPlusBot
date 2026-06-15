using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class PlayerCredentialConfiguration : IEntityTypeConfiguration<PlayerCredential>
{
    public void Configure(EntityTypeBuilder<PlayerCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new
        {
            c.GuildId, c.RustServerId, c.OwnerUserId
        }).IsUnique();
        builder.Property(c => c.ProtectedPlayerToken).IsRequired();

        // Removing a RustServer cascades to its credentials so orphaned secrets cannot linger.
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(c => c.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
