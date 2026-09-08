using RustPlusBot.Domain.Devices;

namespace RustPlusBot.Domain.StorageMonitors;

/// <summary>A paired Smart Storage Monitor the bot manages, surviving restarts. Guild- and server-scoped.</summary>
/// <remarks><see cref="PairedDeviceEntity.Name"/> defaults to a generated "Storage Monitor &lt;EntityId&gt;".</remarks>
public sealed class SmartStorageMonitor : PairedDeviceEntity;
