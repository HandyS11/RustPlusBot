namespace RustPlusBot.Domain.Alarms;

/// <summary>A paired Smart Alarm the bot manages, surviving restarts. Guild- and server-scoped. Notify-only (FCM push).</summary>
public sealed class SmartAlarm
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this alarm belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game smart-alarm entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated "Alarm &lt;EntityId&gt;" (the FCM event carries no name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this alarm's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>When the alarm was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>When true, a fire pings @everyone in #alarms.</summary>
    public bool PingEveryone { get; set; }

    /// <summary>When true, a fire relays the message into in-game team chat.</summary>
    public bool RelayToTeamChat { get; set; }

    /// <summary>The title from the most recent fire, or null if never fired.</summary>
    public string? LastTitle { get; set; }

    /// <summary>The message from the most recent fire, or null if never fired.</summary>
    public string? LastMessage { get; set; }

    /// <summary>When the alarm most recently fired (UTC), or null if never.</summary>
    public DateTimeOffset? LastFiredUtc { get; set; }
}
