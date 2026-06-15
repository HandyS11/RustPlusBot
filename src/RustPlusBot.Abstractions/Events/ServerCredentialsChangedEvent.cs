namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Raised when a server's credential pool changed out-of-band (e.g. a user disconnected their account),
/// so the live connection should be re-evaluated and the #info view refreshed.
/// </summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The affected server.</param>
public sealed record ServerCredentialsChangedEvent(ulong GuildId, Guid ServerId);
