using Persistord.Testing;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Pairing.Tests;

/// <summary>Creates a BotDbContext over a private in-memory SQLite database that lives as long as the test.</summary>
internal static class TestDb
{
    /// <summary>Builds the context and its database, applying the committed migrations.</summary>
    /// <returns>The context and the database backing it. Dispose both.</returns>
    public static (BotDbContext Context, SqliteTestDatabase Database) Create()
    {
        var database = SqliteTestDatabase.Private();
        return (database.CreateContext<BotDbContext>(options => new BotDbContext(options)), database);
    }
}
