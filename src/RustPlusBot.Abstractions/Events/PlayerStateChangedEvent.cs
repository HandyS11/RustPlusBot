using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a team-info poll detects presence transitions since the previous poll.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null if unavailable.</param>
/// <param name="Transitions">The transitions detected this poll (never empty when published).</param>
public sealed record PlayerStateChangedEvent(
    ulong GuildId,
    Guid ServerId,
    MapDimensions? Dimensions,
    IReadOnlyList<PlayerTransition> Transitions);
