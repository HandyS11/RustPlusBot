namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a Rust server is registered to a guild (stub trigger in 1a; FCM pairing in 1b).</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The registered server's id.</param>
public sealed record ServerRegisteredEvent(ulong GuildId, Guid ServerId);
