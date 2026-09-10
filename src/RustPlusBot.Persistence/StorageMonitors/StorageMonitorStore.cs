using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Persistence.Devices;

namespace RustPlusBot.Persistence.StorageMonitors;

/// <summary>EF-backed <see cref="IStorageMonitorStore"/>; every member comes from the shared device store.</summary>
/// <param name="context">The bot database context.</param>
public sealed class StorageMonitorStore(BotDbContext context)
    : PairedDeviceStore<SmartStorageMonitor>(context), IStorageMonitorStore;
