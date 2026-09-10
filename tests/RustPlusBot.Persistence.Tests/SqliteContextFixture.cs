using Persistord.Core.Interception;
using Persistord.Testing;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Creates a BotDbContext over a private in-memory SQLite database that lives as long as the test.</summary>
public static class SqliteContextFixture
{
    /// <summary>
    /// Builds the context and its database. The schema comes from the committed EF Core migrations
    /// (Persistord's default <c>TestSchema.Migrate</c>, not EnsureCreated) so tests exercise the same
    /// schema path the Host uses at startup and catch migration/model drift.
    /// </summary>
    /// <param name="timeProvider">
    /// The clock the <see cref="TimestampInterceptor"/> stamps ICreatedAt/IUpdatedAt rows from.
    /// Defaults to the system clock; pass a <see cref="FixedTimeProvider"/> to assert on timestamps.
    /// </param>
    /// <returns>The context and the database backing it. Dispose both.</returns>
    public static (BotDbContext Context, SqliteTestDatabase Database) Create(TimeProvider? timeProvider = null)
    {
        var database = SqliteTestDatabase.Private();
        var context = database.CreateContext<BotDbContext>(
            options => new BotDbContext(options),
            new TimestampInterceptor(timeProvider ?? TimeProvider.System));
        return (context, database);
    }
}
