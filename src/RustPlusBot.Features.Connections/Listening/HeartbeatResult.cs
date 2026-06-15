namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Classification of a heartbeat probe.</summary>
internal enum HeartbeatKind
{
    /// <summary>Server answered; <see cref="HeartbeatResult.PlayerCount"/> is valid.</summary>
    Ok = 0,

    /// <summary>Server did not answer in time.</summary>
    Unreachable = 1,

    /// <summary>Server rejected the request as unauthorized (token died mid-session).</summary>
    AuthRejected = 2,
}

/// <summary>The outcome of a heartbeat probe.</summary>
/// <param name="Kind">The classification.</param>
/// <param name="PlayerCount">Players online (only meaningful when <see cref="HeartbeatKind.Ok"/>).</param>
/// <remarks><see cref="Ok"/> is a method because it carries a player count; <see cref="Unreachable"/>
/// and <see cref="AuthRejected"/> are constants.</remarks>
internal readonly record struct HeartbeatResult(HeartbeatKind Kind, int PlayerCount)
{
    /// <summary>A healthy heartbeat carrying the player count.</summary>
    /// <param name="playerCount">The number of players currently online.</param>
    public static HeartbeatResult Ok(int playerCount) => new(HeartbeatKind.Ok, playerCount);

    /// <summary>An unreachable heartbeat.</summary>
    public static HeartbeatResult Unreachable { get; } = new(HeartbeatKind.Unreachable, 0);

    /// <summary>An auth-rejected heartbeat.</summary>
    public static HeartbeatResult AuthRejected { get; } = new(HeartbeatKind.AuthRejected, 0);
}
