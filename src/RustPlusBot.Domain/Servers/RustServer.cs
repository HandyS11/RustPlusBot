namespace RustPlusBot.Domain.Servers;

/// <summary>A Rust+ server target bound to a Discord guild. Guild-scoped.</summary>
public sealed class RustServer
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Display name shown in Discord.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Server host (ip or dns).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Rust+ app port.</summary>
    public int Port { get; set; }

    /// <summary>The Discord user who added this server.</summary>
    public ulong AddedByUserId { get; set; }

    /// <summary>The Facepunch server GUID from FCM pairings, backfilled on server pairing; null until first seen. Used to attribute entity pairings.</summary>
    public Guid? FacepunchServerId { get; set; }

    /// <summary>Baseline: the last observed wipe time (UTC) from getInfo, or null before first observation.</summary>
    public DateTimeOffset? LastWipeTimeUtc { get; set; }

    /// <summary>Baseline: the last observed procedural map seed, or null before first observation.</summary>
    public uint? LastMapSeed { get; set; }

    /// <summary>Baseline: the last observed world size (game units), or null before first observation.</summary>
    public uint? LastMapSize { get; set; }
}
