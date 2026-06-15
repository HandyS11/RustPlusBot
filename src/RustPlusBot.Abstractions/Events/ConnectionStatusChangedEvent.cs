namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a server's live-connection state changes, so #info can re-render.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose connection state changed.</param>
public sealed record ConnectionStatusChangedEvent(ulong GuildId, Guid ServerId);
