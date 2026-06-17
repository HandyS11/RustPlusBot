namespace RustPlusBot.Abstractions.Events;

/// <summary>The rig lifecycle boundary an event marks.</summary>
public enum RigEventKind
{
    /// <summary>A CH47 reached the rig — the combat (unlocking) phase began.</summary>
    Activated = 0,

    /// <summary>The combat phase ended — the crate is now lootable.</summary>
    CrateLootable = 1,

    /// <summary>The dormant window ended — the crate respawned and the rig is armed again.</summary>
    Respawned = 2,
}
