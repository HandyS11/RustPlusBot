namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a server's map layer settings change, so the map image repaints immediately.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
public sealed record MapSettingsChangedEvent(ulong GuildId, Guid ServerId);
