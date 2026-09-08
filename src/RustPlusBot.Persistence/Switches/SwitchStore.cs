using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Persistence.Devices;

namespace RustPlusBot.Persistence.Switches;

/// <summary>EF-backed <see cref="ISwitchStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the creation timestamp.</param>
public sealed class SwitchStore(BotDbContext context, IClock clock)
    : PairedDeviceStore<SmartSwitch>(context, clock), ISwitchStore
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
