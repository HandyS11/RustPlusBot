namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// The kind of binary-state smart device behind an entity id. The Rust+ API validates the entity type
/// on reads, so callers must ask for the kind they actually paired (a switch read against an alarm fails).
/// </summary>
public enum SmartDeviceKind
{
    /// <summary>A smart switch.</summary>
    Switch = 0,

    /// <summary>A smart alarm.</summary>
    Alarm = 1,
}
