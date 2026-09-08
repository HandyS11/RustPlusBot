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

    /// <summary>Brings the scope's channels to their declared state and reports where each one lives.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="categoryId">The category the channels sit under.</param>
    /// <param name="culture">The guild's culture, used to localize channel names.</param>
    /// <param name="scope">The workspace scope whose channel specs to reconcile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Discord channel id of every provisioned spec, keyed by channel key.</returns>
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
            if (await IsGatedOffAsync(spec, guildId, serverId, cancellationToken).ConfigureAwait(false))
            {
                gatedOff.Add(spec.Key);
                continue;
            }

            result[spec.Key] = await EnsureChannelAsync(guildId, serverId, categoryId, culture, spec,
                existing.GetValueOrDefault(spec.Key), cancellationToken).ConfigureAwait(false);
        }

        await RemoveGatedOffChannelsAsync(guildId, serverId, gatedOff, existing, cancellationToken)
            .ConfigureAwait(false);
        LogChannelsRetainedOutsideTheRegistry(guildId, specs, existing);
        await ApplyChannelOrderAsync(guildId, categoryId, specs, result, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Decides whether a channel spec is switched off because its capability is unavailable.</summary>
    /// <param name="spec">The channel spec under consideration.</param>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True when the spec declares a capability that is not currently available.</returns>
    private async Task<bool> IsGatedOffAsync(ChannelSpec spec,
        ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken) =>
        spec.Capability is { } capability &&
        !await backends.Registry.IsCapabilityAvailableAsync(capability, guildId, serverId, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Resolves one channel spec to a live, correctly configured and recorded Discord channel.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="categoryId">The category the channel sits under.</param>
    /// <param name="culture">The guild's culture, used to localize the channel name.</param>
    /// <param name="spec">The channel spec to satisfy.</param>
    /// <param name="record">The channel's current provisioning record, or null when it has never been provisioned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The Discord channel id the spec now maps to.</returns>
    private async Task<ulong> EnsureChannelAsync(ulong guildId,
        Guid? serverId,
        ulong categoryId,
        string culture,
        ChannelSpec spec,
        ProvisionedChannel? record,
        CancellationToken cancellationToken)
    {
        var name = localizer.Get(spec.NameKey, culture);
        if (record is not null && backends.Gateway.ChannelExists(guildId, record.DiscordChannelId))
        {
            await backends.Gateway
                .ApplyChannelSettingsAsync(guildId, record.DiscordChannelId, categoryId, name, spec.Permissions,
                    cancellationToken).ConfigureAwait(false);
            return record.DiscordChannelId;
        }

        var channelId = await AdoptOrCreateChannelAsync(guildId, categoryId, name, spec, cancellationToken)
            .ConfigureAwait(false);
        await backends.Store.SaveChannelAsync(
            new ProvisionedChannel
            {
                GuildId = guildId, RustServerId = serverId, ChannelKey = spec.Key, DiscordChannelId = channelId
            },
            cancellationToken).ConfigureAwait(false);
        return channelId;
    }

    /// <summary>Decides between adopting an identically named channel already in the category and creating one.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="categoryId">The category to search in and create under.</param>
    /// <param name="name">The localized channel name.</param>
    /// <param name="spec">The channel spec whose permission profile to apply.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The adopted or newly created Discord channel id.</returns>
    private async Task<ulong> AdoptOrCreateChannelAsync(ulong guildId,
        ulong categoryId,
        string name,
        ChannelSpec spec,
        CancellationToken cancellationToken)
    {
        var adopted = await backends.Gateway.FindChannelAsync(guildId, categoryId, name, cancellationToken)
            .ConfigureAwait(false);
        if (adopted is not ulong adoptedId)
        {
            return await backends.Gateway
                .CreateChannelAsync(guildId, categoryId, name, spec.Permissions, cancellationToken)
                .ConfigureAwait(false);
        }

        await backends.Gateway
            .ApplyChannelSettingsAsync(guildId, adoptedId, categoryId, name, spec.Permissions, cancellationToken)
            .ConfigureAwait(false);
        return adoptedId;
    }

    /// <summary>Deletes the channels whose capability has gone away, along with their provisioning records.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="gatedOff">The channel keys whose capability reported unavailable this pass.</param>
    /// <param name="existing">The scope's provisioning records, keyed by channel key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task RemoveGatedOffChannelsAsync(ulong guildId,
        Guid? serverId,
        IEnumerable<string> gatedOff,
        Dictionary<string, ProvisionedChannel> existing,
        CancellationToken cancellationToken)
    {
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
    }

    /// <summary>Reports the provisioned channels the registry no longer declares; they are kept, not deleted.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="specs">The channel specs the registry declares for this scope.</param>
    /// <param name="existing">The scope's provisioning records, keyed by channel key.</param>
    private void LogChannelsRetainedOutsideTheRegistry(ulong guildId,
        IEnumerable<ChannelSpec> specs,
        Dictionary<string, ProvisionedChannel> existing)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        // Gated-off keys are still in specs, so the retention log below never reports a channel this
        // pass deliberately removed.
        var registryKeys = specs.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var orphan in existing.Keys.Where(k => !registryKeys.Contains(k)))
        {
            logger.LogInformation(
                "Retaining provisioned channel '{Key}' no longer in the registry (guild {GuildId}).", orphan,
                guildId);
        }
    }

    /// <summary>Puts the category's provisioned channels back into the order the specs declare.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="categoryId">The category to order.</param>
    /// <param name="specs">The channel specs the registry declares for this scope.</param>
    /// <param name="provisioned">The Discord channel id of every provisioned spec, keyed by channel key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task ApplyChannelOrderAsync(ulong guildId,
        ulong categoryId,
        IEnumerable<ChannelSpec> specs,
        Dictionary<string, ulong> provisioned,
        CancellationToken cancellationToken)
    {
        // A capability-gated channel created after the rest of the category is appended at the
        // bottom by Discord; restore the declared order. The gateway only issues a reorder call
        // when the live order actually differs, so this is a cache read on the steady state.
        var ordered = specs.Where(s => provisioned.ContainsKey(s.Key))
            .OrderBy(s => s.Order)
            .Select(s => provisioned[s.Key])
            .ToList();
        if (ordered.Count > 1)
        {
            await backends.Gateway.EnsureChannelOrderAsync(guildId, categoryId, ordered, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Brings every declared message in the scope's channels to its rendered state.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="channelIds">The Discord channel id of every provisioned spec, keyed by channel key.</param>
    /// <param name="culture">The guild's culture, handed to each renderer.</param>
    /// <param name="scope">The workspace scope whose message specs to reconcile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
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
            var items = await RenderChannelMessagesAsync(guildId, serverId, channelId, culture, group,
                cancellationToken).ConfigureAwait(false);
            await DeleteMessagesOutOfDeclarationOrderAsync(guildId, channelId, items, cancellationToken)
                .ConfigureAwait(false);
            await PublishChannelMessagesAsync(guildId, serverId, channelId, items, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Renders one channel's declared messages and pairs each with the live message it can reuse.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="channelId">The channel the messages live in.</param>
    /// <param name="culture">The guild's culture, handed to each renderer.</param>
    /// <param name="specs">The channel's message specs, in declaration order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>One item per spec, in declaration order.</returns>
    private async Task<List<MessageItem>> RenderChannelMessagesAsync(ulong guildId,
        Guid? serverId,
        ulong channelId,
        string culture,
        IEnumerable<MessageSpec> specs,
        CancellationToken cancellationToken)
    {
        var items = new List<MessageItem>();

        foreach (var spec in specs)
        {
            var payload = await _renderers[spec.Key]
                .RenderAsync(new MessageRenderContext(guildId, serverId, culture), cancellationToken)
                .ConfigureAwait(false);

            // A renderer with nothing to show (e.g. the source entity vanished mid-reconcile) returns an
            // empty payload; Discord rejects a message with no content/embed/components, so skip it.
            var isEmpty = payload.Text is null && payload.Embed is null && payload.Components is null
                          && payload.Attachment is null;

            var liveId = isEmpty
                ? null
                : await AdoptOrDiscardLiveMessageAsync(guildId, serverId, channelId, spec, payload, cancellationToken)
                    .ConfigureAwait(false);

            items.Add(new MessageItem(spec, payload, isEmpty, liveId));
        }

        return items;
    }

    /// <summary>Decides whether the currently live message can be edited in place, deleting it when it cannot.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="channelId">The channel the message must live in.</param>
    /// <param name="spec">The message spec being reconciled.</param>
    /// <param name="payload">The freshly rendered content the live message would have to carry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The snowflake of the reusable live message, or null when the message has to be posted afresh.</returns>
    private async Task<ulong?> AdoptOrDiscardLiveMessageAsync(ulong guildId,
        Guid? serverId,
        ulong channelId,
        MessageSpec spec,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        var record = await backends.Store.GetMessageAsync(guildId, serverId, spec.Key, cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.DiscordChannelId != channelId)
        {
            return null;
        }

        var live = await backends.Gateway
            .GetLiveMessageAsync(guildId, channelId, record.DiscordMessageId, cancellationToken)
            .ConfigureAwait(false);
        if (live is null)
        {
            return null;
        }

        // An uploaded file cannot be swapped by an edit, so the live message has to carry
        // exactly the file the payload asks for — including none at all. A message that
        // drops its upload, such as a custom-map server whose RustMaps render later
        // verifies, would otherwise keep the stale image alongside its new embed. The file
        // name identifies the content: a message already carrying it is edited in place,
        // which leaves the upload alone (the edit never mentions attachments, and Discord
        // keeps them) while still applying text and embed changes — a culture switch, say.
        if (string.Equals(live.AttachmentFileName, payload.Attachment?.FileName, StringComparison.Ordinal))
        {
            return live.Id;
        }

        await backends.Gateway.DeleteMessageAsync(guildId, channelId, live.Id, cancellationToken)
            .ConfigureAwait(false);
        return null;
    }

    /// <summary>Deletes the live messages that would otherwise render below a message still to be posted.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="channelId">The channel the messages live in.</param>
    /// <param name="items">The channel's items in declaration order; deleted ones are reset to "must post".</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task DeleteMessagesOutOfDeclarationOrderAsync(ulong guildId,
        ulong channelId,
        List<MessageItem> items,
        CancellationToken cancellationToken)
    {
        // Discord orders messages by creation time. If an earlier-declared message still needs to be
        // posted while a later-declared one is already live, the channel would render out of spec
        // order. Delete the live messages after that first to-post one so they re-post fresh, below
        // it, in declaration order. Live messages before it already sit in their correct earlier
        // position and are left untouched.
        var firstToPost = items.FindIndex(i => !i.IsEmpty && i.LiveId is null);
        if (firstToPost < 0)
        {
            return;
        }

        for (var k = firstToPost + 1; k < items.Count; k++)
        {
            if (items[k].LiveId is { } staleId)
            {
                await backends.Gateway.DeleteMessageAsync(guildId, channelId, staleId, cancellationToken)
                    .ConfigureAwait(false);
                items[k] = items[k] with
                {
                    LiveId = null
                };
            }
        }
    }

    /// <summary>Edits or posts every non-empty item and records where it ended up.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="serverId">The Rust server the scope belongs to, or null for the global scope.</param>
    /// <param name="channelId">The channel the messages live in.</param>
    /// <param name="items">The channel's items in declaration order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task PublishChannelMessagesAsync(ulong guildId,
        Guid? serverId,
        ulong channelId,
        IEnumerable<MessageItem> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.IsEmpty)
            {
                continue;
            }

            var messageId = await EditOrPostMessageAsync(guildId, channelId, item, cancellationToken)
                .ConfigureAwait(false);
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

    /// <summary>Decides between editing the item's live message in place and posting a fresh one.</summary>
    /// <param name="guildId">The Discord guild.</param>
    /// <param name="channelId">The channel the message lives in.</param>
    /// <param name="item">The rendered item to materialize.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The snowflake the item is now anchored to.</returns>
    private async Task<ulong> EditOrPostMessageAsync(ulong guildId,
        ulong channelId,
        MessageItem item,
        CancellationToken cancellationToken)
    {
        if (item.LiveId is not { } liveId)
        {
            return await backends.Gateway.PostMessageAsync(guildId, channelId, item.Payload, cancellationToken)
                .ConfigureAwait(false);
        }

        await backends.Gateway.EditMessageAsync(guildId, channelId, liveId, item.Payload, cancellationToken)
            .ConfigureAwait(false);
        return liveId;
    }

    /// <summary>A rendered message spec paired with its current live materialization, if any.</summary>
    /// <param name="Spec">The declared message spec.</param>
    /// <param name="Payload">The freshly rendered content.</param>
    /// <param name="IsEmpty">True if the renderer had nothing to show (skipped entirely).</param>
    /// <param name="LiveId">The snowflake of the currently-live message, or null if it must be posted.</param>
    private sealed record MessageItem(MessageSpec Spec, MessagePayload Payload, bool IsEmpty, ulong? LiveId);
}
