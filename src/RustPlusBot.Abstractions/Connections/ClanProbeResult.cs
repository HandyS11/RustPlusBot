namespace RustPlusBot.Abstractions.Connections;

/// <summary>The outcome of asking a live socket for its clan snapshot.</summary>
public enum ClanProbeStatus
{
    /// <summary>The player is in a clan and the snapshot is populated.</summary>
    HasClan = 0,

    /// <summary>The server answered definitively that the player is in no clan.</summary>
    NoClan = 1,

    /// <summary>The request failed or timed out; the previous known state must be preserved.</summary>
    Unavailable = 2,
}

/// <summary>
/// A clan probe outcome. <see cref="ClanProbeStatus.NoClan"/> and
/// <see cref="ClanProbeStatus.Unavailable"/> are deliberately distinct: collapsing them into a
/// single null snapshot would let a transient socket failure tear down a guild's clan channels.
/// </summary>
/// <param name="Status">What the server said.</param>
/// <param name="Snapshot">The snapshot; non-null if and only if <paramref name="Status"/> is <see cref="ClanProbeStatus.HasClan"/>.</param>
public sealed record ClanProbeResult(ClanProbeStatus Status, ClanSnapshot? Snapshot)
{
    /// <summary>A probe that definitively reported no clan.</summary>
    public static ClanProbeResult NoClan { get; } = new(ClanProbeStatus.NoClan, null);

    /// <summary>A probe that failed; the caller must preserve the last known state.</summary>
    public static ClanProbeResult Unavailable { get; } = new(ClanProbeStatus.Unavailable, null);

    /// <summary>Creates a successful probe carrying <paramref name="snapshot"/>.</summary>
    /// <param name="snapshot">The clan snapshot returned by the server.</param>
    /// <returns>A <see cref="ClanProbeStatus.HasClan"/> result.</returns>
    public static ClanProbeResult From(ClanSnapshot snapshot) => new(ClanProbeStatus.HasClan, snapshot);
}
