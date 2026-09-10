using Persistord.Core;

namespace RustPlusBot.Persistence.Maintenance;

/// <summary>Clears every table's rows while keeping the schema (a live-safe "factory reset").</summary>
/// <param name="context">The bot database context.</param>
public sealed class DatabaseMaintenanceService(BotDbContext context) : IDatabaseMaintenanceService
{
    /// <inheritdoc />
    public Task ClearAllAsync(CancellationToken cancellationToken = default) =>
        // Persistord deletes every mapped table dependents-first in one transaction, so an
        // interruption rolls back rather than leaving the database partially cleared. It issues
        // plain DELETEs through EF, which is why this no longer needs the SQLite-only
        // defer_foreign_keys pragma nor a raw-SQL identifier guard.
        context.ClearAllTablesAsync(cancellationToken);
}
