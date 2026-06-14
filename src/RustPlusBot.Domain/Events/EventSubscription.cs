namespace RustPlusBot.Domain.Events;

/// <summary>A guild's opt-in to a named map/live event (e.g. "CargoShip"). Consumed in subsystem 2.</summary>
public sealed class EventSubscription
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this subscription applies to.</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The event key the guild subscribed to.</summary>
    public string EventKey { get; set; } = string.Empty;
}
