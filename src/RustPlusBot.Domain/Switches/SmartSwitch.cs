using RustPlusBot.Domain.Devices;

namespace RustPlusBot.Domain.Switches;

/// <summary>A paired Smart Switch the bot manages, surviving restarts. Guild- and server-scoped.</summary>
/// <remarks><see cref="PairedDeviceEntity.Name"/> defaults to a generated "Switch &lt;EntityId&gt;".</remarks>
public sealed class SmartSwitch : PairedDeviceEntity
{
    /// <summary>The last observed on/off state.</summary>
    public bool LastIsActive { get; set; }
}
