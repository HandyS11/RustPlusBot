using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Alarms;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Alarms.Relaying;

/// <summary>Loads an alarm from the store, renders it, and posts/edits its embed in #alarms.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="locator">Resolves the #alarms channel id.</param>
/// <param name="poster">Posts/edits alarm embeds.</param>
/// <param name="renderer">Renders alarm embeds.</param>
internal sealed class AlarmRefresher(
    IServiceScopeFactory scopeFactory,
    IAlarmChannelLocator locator,
    IAlarmChannelPoster poster,
    AlarmEmbedRenderer renderer) : IAlarmRefresher
{
    /// <inheritdoc />
    public async Task RefreshAsync(ulong guildId, Guid serverId, ulong entityId, bool unreachable, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var alarm = await store.GetAsync(guildId, serverId, entityId, ct).ConfigureAwait(false);
            if (alarm is null)
            {
                return;
            }

            await RenderAndPostAsync(scope.ServiceProvider, alarm, unreachable, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RefreshAsync(SmartAlarm alarm, bool unreachable, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(alarm);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            await RenderAndPostAsync(scope.ServiceProvider, alarm, unreachable, ct).ConfigureAwait(false);
        }
    }

    private async Task RenderAndPostAsync(
        IServiceProvider provider,
        SmartAlarm alarm,
        bool unreachable,
        CancellationToken ct)
    {
        var channelId = await locator.GetChannelIdAsync(alarm.GuildId, alarm.ServerId, ct).ConfigureAwait(false);
        if (channelId is not { } channel)
        {
            return;
        }

        var culture = await GetCultureAsync(provider, alarm.GuildId, ct).ConfigureAwait(false);
        var (embed, components) = renderer.RenderAlarm(alarm, unreachable, culture);
        var newMessageId = await poster
            .EnsureAsync(channel, alarm.MessageId, embed, components, ct)
            .ConfigureAwait(false);
        if (newMessageId is { } mid && mid != alarm.MessageId)
        {
            var store = provider.GetRequiredService<IAlarmStore>();
            await store.SetMessageIdAsync(alarm.GuildId, alarm.ServerId, alarm.EntityId, mid, ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> GetCultureAsync(
        IServiceProvider provider,
        ulong guildId,
        CancellationToken ct)
    {
        var store = provider.GetRequiredService<IWorkspaceStore>();
        return await store.GetCultureAsync(guildId, ct).ConfigureAwait(false);
    }
}
