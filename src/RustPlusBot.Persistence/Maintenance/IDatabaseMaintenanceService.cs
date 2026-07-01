namespace RustPlusBot.Persistence.Maintenance;

/// <summary>Database-wide maintenance operations.</summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>Deletes every row in every table across all guilds, preserving the schema.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when all rows have been deleted.</returns>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
