namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a reconnected server's wipe baseline no longer matches (the server wiped while we were away or restarting).</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The wiped server id.</param>
/// <param name="PreviousWipeTimeUtc">The baseline wipe time before this wipe, or null if never observed.</param>
/// <param name="NewWipeTimeUtc">The freshly observed wipe time, or null when the server does not report one.</param>
/// <param name="Seed">The new procedural map seed.</param>
/// <param name="WorldSize">The new world size (game units).</param>
public sealed record ServerWipedEvent(
    ulong GuildId,
    Guid ServerId,
    DateTimeOffset? PreviousWipeTimeUtc,
    DateTimeOffset? NewWipeTimeUtc,
    uint Seed,
    uint WorldSize);
