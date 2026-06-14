namespace RustPlusBot.Domain.Guilds;

/// <summary>The bot feature a Discord channel is bound to.</summary>
public enum BoundFeature
{
    /// <summary>Team-chat bridge and in-game commands.</summary>
    Chat = 0,

    /// <summary>Map render and live event notifications.</summary>
    Events = 1,

    /// <summary>Smart switches / alarms / storage monitors.</summary>
    Devices = 2,

    /// <summary>Camera stills and control.</summary>
    Cameras = 3,
}
