using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RustPlusBot.Persistence;

/// <summary>Lets `dotnet-ef` instantiate the context at design time (migrations only).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    /// <inheritdoc />
    public BotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("DataSource=design-time.db")
            .Options;
        return new BotDbContext(options);
    }
}
