using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Events.Classifying;

namespace RustPlusBot.Features.Events.State;

/// <summary>Read access to current map markers and recent events (consumed by in-game command handlers).</summary>
public interface IEventState
{
    /// <summary>Gets the currently-active markers of a kind for a server (empty if none/unknown).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="kind">The marker kind to filter by.</param>
    /// <returns>The active markers of that kind, newest-first.</returns>
    IReadOnlyList<ActiveMarker> GetActiveMarkers(ulong guildId, Guid serverId, MarkerKind kind);

    /// <summary>Gets the recent events for a server, newest-first (empty if none/unknown).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <returns>The recent events, newest-first.</returns>
    IReadOnlyList<RustMapEvent> GetRecentEvents(ulong guildId, Guid serverId);
}
