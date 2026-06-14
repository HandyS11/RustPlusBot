using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Connections;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ConnectionStateConfiguration : IEntityTypeConfiguration<ConnectionState>
{
    public void Configure(EntityTypeBuilder<ConnectionState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.RustServerId);
    }
}
