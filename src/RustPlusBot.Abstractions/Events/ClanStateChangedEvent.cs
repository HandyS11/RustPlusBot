using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Published when a server's clan snapshot may have changed — on connect (probe) and on every
/// in-game clan change. Carries the probe status so consumers can tell "definitely no clan"
/// apart from "could not ask".
/// </summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server the snapshot belongs to.</param>
/// <param name="Status">Whether the player has a clan, has none, or could not be asked.</param>
/// <param name="Snapshot">The new snapshot; non-null if and only if <paramref name="Status"/> is <see cref="ClanProbeStatus.HasClan"/>.</param>
public sealed record ClanStateChangedEvent(
    ulong GuildId,
    Guid ServerId,
    ClanProbeStatus Status,
    ClanSnapshot? Snapshot);
