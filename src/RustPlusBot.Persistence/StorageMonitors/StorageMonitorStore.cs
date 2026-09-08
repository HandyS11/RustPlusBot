using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Persistence.Devices;

namespace RustPlusBot.Persistence.StorageMonitors;

/// <summary>EF-backed <see cref="IStorageMonitorStore"/>; every member comes from the shared device store.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the creation timestamp.</param>
public sealed class StorageMonitorStore(BotDbContext context, IClock clock)
    : PairedDeviceStore<SmartStorageMonitor>(context, clock), IStorageMonitorStore;
