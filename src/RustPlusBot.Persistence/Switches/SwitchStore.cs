using RustPlusBot.Domain.Switches;
using RustPlusBot.Persistence.Devices;

namespace RustPlusBot.Persistence.Switches;

/// <summary>EF-backed <see cref="ISwitchStore"/>.</summary>
/// <param name="context">The bot database context.</param>
public sealed class SwitchStore(BotDbContext context)
    : PairedDeviceStore<SmartSwitch>(context), ISwitchStore
{
    /// <inheritdoc />
    public Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        MutateAsync(guildId, serverId, entityId, s => s.LastIsActive = isActive, cancellationToken);
}
