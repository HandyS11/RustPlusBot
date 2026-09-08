using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Domain.Switches;

namespace RustPlusBot.Persistence.Switches;

/// <summary>Persists managed Smart Switches (accepted pairings only; pending pairings stay in-memory).</summary>
public interface ISwitchStore : IPairedDeviceStore<SmartSwitch>
{
    /// <summary>Updates the last-known on/off state (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="isActive">The last observed on/off state.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        CancellationToken cancellationToken = default);
}
