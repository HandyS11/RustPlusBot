using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Domain.StorageMonitors;

namespace RustPlusBot.Persistence.StorageMonitors;

/// <summary>
///     Persists managed Smart Storage Monitors (accepted pairings only; pending pairings stay
///     in-memory). Adds nothing to <see cref="IPairedDeviceStore{TEntity}"/> — it exists so the
///     monitor feature can resolve its own store from DI without naming the generic base.
/// </summary>
public interface IStorageMonitorStore : IPairedDeviceStore<SmartStorageMonitor>;
