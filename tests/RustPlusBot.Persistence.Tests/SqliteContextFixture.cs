using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Persistence;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Creates a BotDbContext over a private in-memory SQLite connection kept open for the test.</summary>
public static class SqliteContextFixture
{
    public static (BotDbContext Context, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new BotDbContext(options);
        // Apply the committed EF Core migrations (not EnsureCreated) so tests exercise the same
        // schema path the Host uses at startup, catching migration/model drift.
        context.Database.Migrate();
        return (context, connection);
    }
}
