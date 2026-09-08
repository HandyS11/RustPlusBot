namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// The identity carried by every "a smart device was paired in-game" event. Lets the shared pairing
/// coordinator address a pending device without knowing which device type raised the event.
/// </summary>
public interface IPairedDeviceEvent
{
    /// <summary>Gets the owning Discord guild snowflake.</summary>
    ulong GuildId { get; }

    /// <summary>Gets the local Rust server id the entity belongs to.</summary>
    Guid ServerId { get; }

    /// <summary>Gets the in-game entity id of the paired device.</summary>
    ulong EntityId { get; }
}
