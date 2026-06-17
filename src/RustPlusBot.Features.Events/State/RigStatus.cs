namespace RustPlusBot.Features.Events.State;

/// <summary>The inferred lifecycle status of an oil rig.</summary>
public enum RigStatus
{
    /// <summary>Crate present and armed; the resting state (also assumed before any activation is seen).</summary>
    Online = 0,

    /// <summary>Combat phase — scientists deployed, crate unlocking (becomes lootable at the end).</summary>
    Active = 1,

    /// <summary>Crate became lootable and (in practice) was looted; the rig is dormant until respawn.</summary>
    Offline = 2,
}
