using Microsoft.Extensions.Logging;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Desired-state reconciler. resolve -> adopt -> create, serialized per guild.</summary>
/// <param name="registry">The workspace channel/message spec registry.</param>
/// <param name="gateway">The Discord gateway adapter.</param>
/// <param name="store">The provisioning persistence store.</param>
/// <param name="renderers">All registered message renderers.</param>
/// <param name="servers">The server persistence service.</param>
/// <param name="localizer">The localization service.</param>
/// <param name="provisioningLock">The per-guild provisioning lock.</param>
/// <param name="logger">The logger.</param>
internal sealed class WorkspaceReconciler(
    IWorkspaceRegistry registry,
    IWorkspaceGateway gateway,
    IWorkspaceStore store,
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
        var missing = gateway.GetMissingBotPermissions(guildId);
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
        var missing = gateway.GetMissingBotPermissions(guildId);
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
        var categories = await store.GetAllCategoriesAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (categories.Count == 0)
        {
            return;
        }

        if (gateway.GetMissingBotPermissions(guildId).Count > 0)
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
        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
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

        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
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
        var record = await store.GetCategoryAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (record is not null && gateway.CategoryExists(guildId, record.DiscordCategoryId))
        {
            return record.DiscordCategoryId;
        }

        var adopted = await gateway.FindCategoryAsync(guildId, name, cancellationToken).ConfigureAwait(false);
        var categoryId = adopted ??
                         await gateway.CreateCategoryAsync(guildId, name, cancellationToken).ConfigureAwait(false);
        await store.SaveCategoryAsync(
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
        var existing = (await store.GetChannelsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.ChannelKey, StringComparer.Ordinal);
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var specs = registry.GetChannelSpecs(scope);

        foreach (var spec in specs)
        {
            var name = localizer.Get(spec.NameKey, culture);
            ulong channelId;

            if (existing.TryGetValue(spec.Key, out var rec) && gateway.ChannelExists(guildId, rec.DiscordChannelId))
            {
                channelId = rec.DiscordChannelId;
                await gateway
                    .ApplyChannelSettingsAsync(guildId, channelId, categoryId, name, spec.Permissions,
                        cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var adopted = await gateway.FindChannelAsync(guildId, categoryId, name, cancellationToken)
                    .ConfigureAwait(false);
                if (adopted is ulong adoptedId)
                {
                    channelId = adoptedId;
                    await gateway
                        .ApplyChannelSettingsAsync(guildId, channelId, categoryId, name, spec.Permissions,
                            cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    channelId = await gateway
                        .CreateChannelAsync(guildId, categoryId, name, spec.Permissions, cancellationToken)
                        .ConfigureAwait(false);
                }

                await store.SaveChannelAsync(
                    new ProvisionedChannel
                    {
                        GuildId = guildId, RustServerId = serverId, ChannelKey = spec.Key, DiscordChannelId = channelId
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            result[spec.Key] = channelId;
        }

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

        return result;
    }

    private async Task EnsureMessagesAsync(ulong guildId,
        Guid? serverId,
        Dictionary<string, ulong> channelIds,
        string culture,
        WorkspaceScope scope,
        CancellationToken cancellationToken)
    {
        foreach (var spec in registry.GetMessageSpecs(scope))
        {
            if (!channelIds.TryGetValue(spec.ChannelKey, out var channelId) ||
                !_renderers.TryGetValue(spec.Key, out var renderer))
            {
                continue;
            }

            var payload = await renderer
                .RenderAsync(new MessageRenderContext(guildId, serverId, culture), cancellationToken)
                .ConfigureAwait(false);

            // A renderer with nothing to show (e.g. the source entity vanished mid-reconcile) returns an
            // empty payload; Discord rejects a message with no content/embed/components, so skip it.
            if (payload.Text is null && payload.Embed is null && payload.Components is null)
            {
                continue;
            }

            var record = await store.GetMessageAsync(guildId, serverId, spec.Key, cancellationToken)
                .ConfigureAwait(false);

            var canEditInPlace = record is not null
                                 && record.DiscordChannelId == channelId
                                 && await gateway
                                     .MessageExistsAsync(guildId, channelId, record.DiscordMessageId, cancellationToken)
                                     .ConfigureAwait(false);

            if (canEditInPlace)
            {
                await gateway.EditMessageAsync(guildId, channelId, record!.DiscordMessageId, payload, cancellationToken)
                    .ConfigureAwait(false);
                await store.SaveMessageAsync(
                    new ProvisionedMessage
                    {
                        GuildId = guildId,
                        RustServerId = serverId,
                        MessageKey = spec.Key,
                        DiscordChannelId = channelId,
                        DiscordMessageId = record.DiscordMessageId
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var messageId = await gateway.PostMessageAsync(guildId, channelId, payload, cancellationToken)
                    .ConfigureAwait(false);
                await store.SaveMessageAsync(
                    new ProvisionedMessage
                    {
                        GuildId = guildId,
                        RustServerId = serverId,
                        MessageKey = spec.Key,
                        DiscordChannelId = channelId,
                        DiscordMessageId = messageId
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
