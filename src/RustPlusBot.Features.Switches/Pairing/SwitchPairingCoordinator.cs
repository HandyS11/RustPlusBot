using Discord;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Devices.Pairing;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Switches.Pairing;

/// <summary>Turns a <see cref="SwitchPairedEvent"/> into an "Add it?" prompt and, on Accept, a managed switch.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped switch/workspace stores.</param>
/// <param name="locator">Resolves the #switches channel id.</param>
/// <param name="poster">Posts/edits switch + prompt messages.</param>
/// <param name="renderer">Renders the prompt and switch embeds.</param>
internal sealed class SwitchPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    ISwitchChannelLocator locator,
    ISwitchChannelPoster poster,
    SwitchEmbedRenderer renderer)
    : PairedDeviceCoordinator<SwitchPairedEvent, SmartSwitch>(scopeFactory, locator, poster)
{
    /// <inheritdoc />
    protected override string DefaultName(ulong entityId) => $"Switch {entityId}";

    /// <inheritdoc />
    protected override (Embed Embed, MessageComponent Components) RenderPrompt(
        Guid serverId,
        ulong entityId,
        string defaultName,
        string culture)
        => renderer.RenderPrompt(serverId, entityId, defaultName, culture);

    /// <inheritdoc />
    /// <remarks>
    /// State is unknown until the next prime/trigger, so this renders the persisted
    /// <see cref="SmartSwitch.LastIsActive"/> (defaults false) rather than guessing.
    /// </remarks>
    protected override (Embed Embed, MessageComponent Components) RenderAccepted(SmartSwitch entity, string culture)
        => renderer.RenderSwitch(entity, isActive: entity.LastIsActive, culture);

    /// <inheritdoc />
    protected override IPairedDeviceStore<SmartSwitch> Store(IServiceProvider services) =>
        services.GetRequiredService<ISwitchStore>();
}
