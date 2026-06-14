using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            // Suppress PendingModelChangesWarning while a migration for the latest model changes
            // is pending (i.e., between removing types from the context and adding the migration
            // that drops the corresponding table). The next task adds the migration; until then
            // the in-memory test schema is still valid for all remaining tests.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        var context = new BotDbContext(options);
        // Apply the committed EF Core migrations (not EnsureCreated) so tests exercise the same
        // schema path the Host uses at startup, catching migration/model drift.
        context.Database.Migrate();
        return (context, connection);
    }
}
