using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace RustPlusBot.Persistence.Maintenance;

/// <summary>Clears every table's rows while keeping the schema (a live-safe "factory reset").</summary>
/// <param name="context">The bot database context.</param>
public sealed class DatabaseMaintenanceService(BotDbContext context) : IDatabaseMaintenanceService
{
    /// <inheritdoc />
    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var tables = context.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Wipe every table in one transaction so an interruption rolls back rather than leaving the
        // database partially cleared. defer_foreign_keys defers FK enforcement to commit time (and
        // resets itself when the transaction ends), so tables can be cleared in any order — once every
        // table is empty the commit-time check has nothing to violate. This also avoids leaving a
        // connection-level foreign_keys pragma toggled off on a pooled connection.
        var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = ON", cancellationToken)
                .ConfigureAwait(false);

            foreach (var table in tables)
            {
                // Table names come from the EF model (never user input). Fail loud on an unexpected
                // identifier rather than silently skipping it and reporting a misleading success; the
                // guard also keeps the raw statement demonstrably injection-safe for the Sonar gate.
                if (!IsSafeIdentifier(table!))
                {
                    throw new InvalidOperationException(
                        string.Create(CultureInfo.InvariantCulture,
                            $"Refusing to clear table with an unexpected identifier: '{table}'."));
                }

                var sql = string.Create(CultureInfo.InvariantCulture, $"DELETE FROM \"{table}\"");
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsSafeIdentifier(string identifier) =>
        identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
}
