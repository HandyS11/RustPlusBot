namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a server's live-connection state changes, so #info can re-render.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose connection state changed.</param>
/// <param name="IsConnected">True when the new status is Connected.</param>
/// <param name="WasConnected">
///     True when the previous status published in this process was Connected. Deliberately
///     in-process (not store-derived): the persisted status survives restarts and would still
///     read Connected right after boot, re-triggering unreachable sweeps on every startup.
/// </param>
public sealed record ConnectionStatusChangedEvent(
    ulong GuildId,
    Guid ServerId,
    bool IsConnected,
    bool WasConnected);
