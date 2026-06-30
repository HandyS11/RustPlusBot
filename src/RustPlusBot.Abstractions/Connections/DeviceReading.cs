namespace RustPlusBot.Abstractions.Connections;

/// <summary>A smart-switch/alarm read: on/off state plus reachability. <see cref="IsActive"/> is null unless <see cref="Reachability"/> is Reachable.</summary>
/// <param name="IsActive">The device on/off state; null unless Reachable.</param>
/// <param name="Reachability">The device reachability.</param>
public readonly record struct DeviceReading(bool? IsActive, DeviceReachability Reachability);

/// <summary>A storage-monitor read: contents plus reachability. <see cref="Contents"/> is null unless <see cref="Reachability"/> is Reachable.</summary>
/// <param name="Contents">The storage contents snapshot; null unless Reachable.</param>
/// <param name="Reachability">The device reachability.</param>
public readonly record struct StorageReading(StorageContentsSnapshot? Contents, DeviceReachability Reachability);
