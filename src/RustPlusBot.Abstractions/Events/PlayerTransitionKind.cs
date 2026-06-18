namespace RustPlusBot.Abstractions.Events;

/// <summary>The kind of team-member presence transition detected between two team snapshots.</summary>
public enum PlayerTransitionKind
{
    /// <summary>Member came online.</summary>
    Connect = 0,

    /// <summary>Member went offline.</summary>
    Disconnect = 1,

    /// <summary>Member died.</summary>
    Death = 2,

    /// <summary>Member spawned/respawned.</summary>
    Respawn = 3,

    /// <summary>Member crossed the AFK stillness threshold.</summary>
    BecameAfk = 4,

    /// <summary>Member moved again after being AFK.</summary>
    ReturnedFromAfk = 5,
}
