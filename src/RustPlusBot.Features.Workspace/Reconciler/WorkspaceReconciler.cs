using Microsoft.Extensions.Logging;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Bundles the storage-backend collaborators injected into <see cref="WorkspaceReconciler"/>.</summary>
/// <param name="Registry">The workspace channel/message spec registry.</param>
/// <param name="Gateway">The Discord gateway adapter.</param>
/// <param name="Store">The provisioning persistence store.</param>
internal sealed record WorkspaceBackends(
    IWorkspaceRegistry Registry,
    IWorkspaceGateway Gateway,
    IWorkspaceStore Store);

/// <summary>Desired-state reconciler. resolve -> adopt -> create, serialized per guild.</summary>
/// <param name="backends">Bundles the storage-backend collaborators.</param>
/// <param name="renderers">All registered message renderers.</param>
/// <param name="servers">The server persistence service.</param>
/// <param name="localizer">The localization service.</param>
/// <param name="provisioningLock">The per-guild provisioning lock.</param>
/// <param name="logger">The logger.</param>
internal sealed class WorkspaceReconciler(
    WorkspaceBackends backends,
    IEnumerable<IMessageRenderer> renderers,
    IServerService servers,
    ILocalizer localizer,
    IProvisioningLock provisioningLock,
    ILogger<WorkspaceReconciler> logger) : IWorkspaceReconciler
{
    private readonly Dictionary<string, IMessageRenderer> _renderers =
        renderers.ToDictionary(r => r.MessageKey, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<ReconcileResult> ReconcileGlobalAsync(ulong guildId,
        CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        var missing = backends.Gateway.GetMissingBotPermissions(guildId);
        if (missing.Count > 0)
        {
            return ReconcileResult.Missing(missing);
        }

        await ReconcileGlobalCoreAsync(guildId, cancellationToken).ConfigureAwait(false);
        return ReconcileResult.Provisioned;
    }

    /// <inheritdoc />
    public async Task<ReconcileResult> ReconcileServerAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        var missing = backends.Gateway.GetMissingBotPermissions(guildId);
        if (missing.Count > 0)
        {
            return ReconcileResult.Missing(missing);
        }

        var provisioned = await ReconcileServerCoreAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return provisioned ? ReconcileResult.Provisioned : ReconcileResult.Skipped;
    }

    /// <inheritdoc />
    public async Task HealGuildAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);

        // Heal only the scopes that are actually provisioned; never resurrect a workspace cleared by
        // reset (no categories) and never create a scope the guild never asked for.
        var categories = await backends.Store.GetAllCategoriesAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (categories.Count == 0)
        {
            return;
        }

        if (backends.Gateway.GetMissingBotPermissions(guildId).Count > 0)
        {
            return;
        }

        if (categories.Any(c => c.RustServerId is null))
        {
            await ReconcileGlobalCoreAsync(guildId, cancellationToken).ConfigureAwait(false);
        }

        foreach (var category in categories.Where(c => c.RustServerId is not null))
        {
            await ReconcileServerCoreAsync(guildId, category.RustServerId!.Value, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ReconcileGlobalCoreAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var culture = await backends.Store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        var categoryName = localizer.Get("category.global.name", culture);
        await ReconcileScopeAsync(guildId, null, categoryName, culture, WorkspaceScope.Global, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> ReconcileServerCoreAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var server = await servers.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            logger.LogWarning("ReconcileServer skipped: server {ServerId} not found in guild {GuildId}.", serverId,
                guildId);
            return false;
        }

        var culture = await backends.Store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        await ReconcileScopeAsync(guildId, serverId, server.Name, culture, WorkspaceScope.PerServer, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task ReconcileScopeAsync(ulong guildId,
        Guid? serverId,
        string categoryName,
        string culture,
        WorkspaceScope scope,
        CancellationToken cancellationToken)
    {
        var categoryId = await EnsureCategoryAsync(guildId, serverId, categoryName, cancellationToken)
            .ConfigureAwait(false);
        var channelIds = await EnsureChannelsAsync(guildId, serverId, categoryId, culture, scope, cancellationToken)
            .ConfigureAwait(false);
        await EnsureMessagesAsync(guildId, serverId, channelIds, culture, scope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ulong> EnsureCategoryAsync(ulong guildId,
        Guid? serverId,
        string name,
        CancellationToken cancellationToken)
    {
        var record = await backends.Store.GetCategoryAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (record is not null && backends.Gateway.CategoryExists(guildId, record.DiscordCategoryId))
        {
            return record.DiscordCategoryId;
        }

        var adopted = await backends.Gateway.FindCategoryAsync(guildId, name, cancellationToken).ConfigureAwait(false);
        var categoryId = adopted ??
                         await backends.Gateway.CreateCategoryAsync(guildId, name, cancellationToken)
                             .ConfigureAwait(false);
        await backends.Store.SaveCategoryAsync(
            new ProvisionedCategory
            {
                GuildId = guildId, RustServerId = serverId, DiscordCategoryId = categoryId
            },
            cancellationToken).ConfigureAwait(false);
        return categoryId;
    }

    private async Task<Dictionary<string, ulong>> EnsureChannelsAsync(ulong guildId,
        Guid? serverId,
        ulong categoryId,
        string culture,
        WorkspaceScope scope,
        CancellationToken cancellationToken)
    {
        var existing =
            (await backends.Store.GetChannelsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.ChannelKey, StringComparer.Ordinal);
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var specs = backends.Registry.GetChannelSpecs(scope);
        var gatedOff = new List<string>();

        foreach (var spec in specs)
        {
            if (spec.Capability is { } capability &&
                !await backends.Registry
                    .IsCapabilityAvailableAsync(capability, guildId, serverId, cancellationToken)
                    .ConfigureAwait(false))
            {
                gatedOff.Add(spec.Key);
                continue;
            }

            var name = localizer.Get(spec.NameKey, culture);
            ulong channelId;

            if (existing.TryGetValue(spec.Key, out var rec) &&
                backends.Gateway.ChannelExists(guildId, rec.DiscordChannelId))
            {
                channelId = rec.DiscordChannelId;
                await backends.Gateway
                    .ApplyChannelSettingsAsync(guildId, channelId, categoryId, name, spec.Permissions,
                        cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var adopted = await backends.Gateway.FindChannelAsync(guildId, categoryId, name, cancellationToken)
                    .ConfigureAwait(false);
                if (adopted is ulong adoptedId)
                {
                    channelId = adoptedId;
                    await backends.Gateway
                        .ApplyChannelSettingsAsync(guildId, channelId, categoryId, name, spec.Permissions,
                            cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    channelId = await backends.Gateway
                        .CreateChannelAsync(guildId, categoryId, name, spec.Permissions, cancellationToken)
                        .ConfigureAwait(false);
                }

                await backends.Store.SaveChannelAsync(
                    new ProvisionedChannel
                    {
                        GuildId = guildId, RustServerId = serverId, ChannelKey = spec.Key, DiscordChannelId = channelId
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            result[spec.Key] = channelId;
        }

        // A capability that has gone away is an explicit removal, distinct from a spec merely
        // disappearing from the registry (which is retained, below).
        foreach (var key in gatedOff)
        {
            if (!existing.TryGetValue(key, out var stale))
            {
                continue;
            }

            await backends.Gateway.DeleteChannelAsync(guildId, stale.DiscordChannelId, cancellationToken)
                .ConfigureAwait(false);
            await backends.Store.DeleteChannelAsync(guildId, serverId, key, cancellationToken).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Removed channel '{Key}' for guild {GuildId}: its capability is no longer available.", key,
                    guildId);
            }
        }

        // Gated-off keys are still in specs, so the retention log below never reports a channel this
        // pass deliberately removed.
        var registryKeys = specs.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        if (logger.IsEnabled(LogLevel.Information))
        {
            foreach (var orphan in existing.Keys.Where(k => !registryKeys.Contains(k)))
            {
                logger.LogInformation(
                    "Retaining provisioned channel '{Key}' no longer in the registry (guild {GuildId}).", orphan,
                    guildId);
            }
        }

        // A capability-gated channel created after the rest of the category is appended at the
        // bottom by Discord; restore the declared order. The gateway only issues a reorder call
        // when the live order actually differs, so this is a cache read on the steady state.
        var ordered = specs.Where(s => result.ContainsKey(s.Key))
            .OrderBy(s => s.Order)
            .Select(s => result[s.Key])
            .ToList();
        if (ordered.Count > 1)
        {
            await backends.Gateway.EnsureChannelOrderAsync(guildId, categoryId, ordered, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task EnsureMessagesAsync(ulong guildId,
        Guid? serverId,
        Dictionary<string, ulong> channelIds,
        string culture,
        WorkspaceScope scope,
        CancellationToken cancellationToken)
    {
        var specsByChannel = backends.Registry.GetMessageSpecs(scope)
            .Where(s => channelIds.ContainsKey(s.ChannelKey) && _renderers.ContainsKey(s.Key))
            .GroupBy(s => s.ChannelKey, StringComparer.Ordinal);

        foreach (var group in specsByChannel)
        {
            var channelId = channelIds[group.Key];
            var items = new List<MessageItem>();

            foreach (var spec in group)
            {
                var payload = await _renderers[spec.Key]
                    .RenderAsync(new MessageRenderContext(guildId, serverId, culture), cancellationToken)
                    .ConfigureAwait(false);

                // A renderer with nothing to show (e.g. the source entity vanished mid-reconcile) returns an
                // empty payload; Discord rejects a message with no content/embed/components, so skip it.
                var isEmpty = payload.Text is null && payload.Embed is null && payload.Components is null
                              && payload.Attachment is null;

                ulong? liveId = null;
                var upToDate = false;
                if (!isEmpty)
                {
                    var record = await backends.Store.GetMessageAsync(guildId, serverId, spec.Key, cancellationToken)
                        .ConfigureAwait(false);
                    var live = record is not null && record.DiscordChannelId == channelId
                        ? await backends.Gateway
                            .GetLiveMessageAsync(guildId, channelId, record.DiscordMessageId, cancellationToken)
                            .ConfigureAwait(false)
                        : null;
                    if (live is not null)
                    {
                        liveId = live.Id;

                        // An uploaded file cannot be swapped by an edit, so the live message has to carry
                        // exactly the file the payload asks for — including none at all. A message that
                        // drops its upload, such as a custom-map server whose RustMaps render later
                        // verifies, would otherwise keep the stale image alongside its new embed. The file
                        // name identifies the content, so an unchanged one means "already posted, leave it
                        // entirely alone": re-uploading costs its full size per server per reconcile pass.
                        if (!string.Equals(live.AttachmentFileName, payload.Attachment?.FileName,
                                StringComparison.Ordinal))
                        {
                            await backends.Gateway
                                .DeleteMessageAsync(guildId, channelId, live.Id, cancellationToken)
                                .ConfigureAwait(false);
                            liveId = null;
                        }
                        else if (payload.Attachment is not null)
                        {
                            upToDate = true;
                        }
                    }
                }

                items.Add(new MessageItem(spec, payload, isEmpty, liveId, upToDate));
            }

            // Discord orders messages by creation time. If an earlier-declared message still needs to be
            // posted while a later-declared one is already live, the channel would render out of spec
            // order. Delete the live messages after that first to-post one so they re-post fresh, below
            // it, in declaration order. Live messages before it already sit in their correct earlier
            // position and are left untouched.
            var firstToPost = items.FindIndex(i => !i.IsEmpty && i.LiveId is null);
            if (firstToPost >= 0)
            {
                for (var k = firstToPost + 1; k < items.Count; k++)
                {
                    if (items[k].LiveId is { } staleId)
                    {
                        await backends.Gateway.DeleteMessageAsync(guildId, channelId, staleId, cancellationToken)
                            .ConfigureAwait(false);
                        items[k] = items[k] with
                        {
                            LiveId = null, UpToDate = false
                        };
                    }
                }
            }

            foreach (var item in items)
            {
                if (item.IsEmpty || item.UpToDate)
                {
                    continue;
                }

                ulong messageId;
                if (item.LiveId is { } liveId)
                {
                    await backends.Gateway
                        .EditMessageAsync(guildId, channelId, liveId, item.Payload, cancellationToken)
                        .ConfigureAwait(false);
                    messageId = liveId;
                }
                else
                {
                    messageId = await backends.Gateway
                        .PostMessageAsync(guildId, channelId, item.Payload, cancellationToken)
                        .ConfigureAwait(false);
                }

                await backends.Store.SaveMessageAsync(
                    new ProvisionedMessage
                    {
                        GuildId = guildId,
                        RustServerId = serverId,
                        MessageKey = item.Spec.Key,
                        DiscordChannelId = channelId,
                        DiscordMessageId = messageId
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>A rendered message spec paired with its current live materialization, if any.</summary>
    /// <param name="Spec">The declared message spec.</param>
    /// <param name="Payload">The freshly rendered content.</param>
    /// <param name="IsEmpty">True if the renderer had nothing to show (skipped entirely).</param>
    /// <param name="LiveId">The snowflake of the currently-live message, or null if it must be posted.</param>
    /// <param name="UpToDate">True if the live message already carries this payload's attachment (leave it alone).</param>
    private sealed record MessageItem(
        MessageSpec Spec,
        MessagePayload Payload,
        bool IsEmpty,
        ulong? LiveId,
        bool UpToDate = false);
}
