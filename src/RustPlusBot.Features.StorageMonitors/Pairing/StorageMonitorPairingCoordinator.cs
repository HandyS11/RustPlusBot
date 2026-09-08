using Discord;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Devices;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.Devices.Pairing;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Features.StorageMonitors.Pairing;

/// <summary>Turns a <see cref="StorageMonitorPairedEvent"/> into an "Add it?" prompt and, on Accept, a managed storage monitor.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped storage monitor/workspace stores.</param>
/// <param name="locator">Resolves the #storagemonitors channel id.</param>
/// <param name="poster">Posts/edits storage monitor + prompt messages.</param>
/// <param name="renderer">Renders the prompt and storage monitor embeds.</param>
internal sealed class StorageMonitorPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    IStorageMonitorChannelLocator locator,
    IStorageMonitorChannelPoster poster,
    StorageMonitorEmbedRenderer renderer)
    : PairedDeviceCoordinator<StorageMonitorPairedEvent, SmartStorageMonitor>(scopeFactory, locator, poster)
{
    /// <inheritdoc />
    protected override string DefaultName(ulong entityId) => $"Storage Monitor {entityId}";

    /// <inheritdoc />
    protected override (Embed Embed, MessageComponent Components) RenderPrompt(
        Guid serverId,
        ulong entityId,
        string defaultName,
        string culture)
        => renderer.RenderPrompt(serverId, entityId, defaultName, culture);

    /// <inheritdoc />
    /// <remarks>
    /// Contents are unknown until the next prime/trigger, so this renders with
    /// <c>contents: null</c> (unreachable) rather than showing a stale inventory.
    /// </remarks>
    protected override (Embed Embed, MessageComponent Components) RenderAccepted(
        SmartStorageMonitor entity,
        string culture)
        => renderer.RenderMonitor(entity, contents: null, culture);

    /// <inheritdoc />
    protected override IPairedDeviceStore<SmartStorageMonitor> Store(IServiceProvider services) =>
        services.GetRequiredService<IStorageMonitorStore>();
}
