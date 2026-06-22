using RustPlusBot.Domain.Alarms;

namespace RustPlusBot.Features.Alarms.Relaying;

/// <summary>Re-renders a single alarm's embed on demand (prime, reconnect, or trigger).</summary>
public interface IAlarmRefresher
{
    /// <summary>Loads the alarm, renders it, and posts or edits its embed.</summary>
    /// <param name="guildId">The owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="unreachable">When true the alarm entity is currently unreachable.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been refreshed (or no-op if alarm/channel absent).</returns>
    Task RefreshAsync(ulong guildId, Guid serverId, ulong entityId, bool unreachable, CancellationToken ct);

    /// <summary>
    /// Renders an already-loaded alarm and posts or edits its embed, skipping the store re-fetch. Use when the
    /// caller already holds the alarm (e.g. a batch reconnect refresh) to avoid an extra per-alarm query.
    /// </summary>
    /// <param name="alarm">The alarm to render.</param>
    /// <param name="unreachable">When true the alarm entity is currently unreachable.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been refreshed (or no-op if the channel is absent).</returns>
    Task RefreshAsync(SmartAlarm alarm, bool unreachable, CancellationToken ct);
}
