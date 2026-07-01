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

        // Keep one connection open across every statement so the FK pragma persists
        // (with per-statement connections the pragma would reset before the DELETEs).
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", cancellationToken)
                .ConfigureAwait(false);

            foreach (var table in tables)
            {
                // Deletion order is deliberately irrelevant: PRAGMA foreign_keys = OFF (above) suspends
                // FK enforcement for the wipe, so do not "fix" this by adding a topological sort.
                // Table names come from the EF model (never user input); the identifier guard keeps
                // the raw statement demonstrably injection-safe for the Sonar gate.
                if (!IsSafeIdentifier(table!))
                {
                    continue;
                }

                var sql = string.Create(CultureInfo.InvariantCulture, $"DELETE FROM \"{table}\"");
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }

            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static bool IsSafeIdentifier(string identifier) =>
        identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
}
