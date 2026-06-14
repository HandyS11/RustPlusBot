using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ProvisionedCategoryConfiguration : IEntityTypeConfiguration<ProvisionedCategory>
{
    public void Configure(EntityTypeBuilder<ProvisionedCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new
        {
            c.GuildId, c.RustServerId
        }).IsUnique();
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(c => c.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
