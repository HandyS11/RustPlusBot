namespace RustPlusBot.Domain.Entities;

/// <summary>The kind of paired in-game smart device.</summary>
public enum PairedEntityKind
{
    /// <summary>A smart switch.</summary>
    SmartSwitch = 0,

    /// <summary>A smart alarm.</summary>
    SmartAlarm = 1,

    /// <summary>A storage monitor.</summary>
    StorageMonitor = 2,
}
