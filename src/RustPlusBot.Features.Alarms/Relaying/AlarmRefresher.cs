using Microsoft.Extensions.DependencyInjection;
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

            var channelId = await locator.GetChannelIdAsync(guildId, serverId, ct).ConfigureAwait(false);
            if (channelId is not { } channel)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, guildId, ct).ConfigureAwait(false);
            var (embed, components) = renderer.RenderAlarm(alarm, unreachable, culture);
            var newMessageId = await poster
                .EnsureAsync(channel, alarm.MessageId, embed, components, ct)
                .ConfigureAwait(false);
            if (newMessageId is { } mid && mid != alarm.MessageId)
            {
                await store.SetMessageIdAsync(guildId, serverId, entityId, mid, ct).ConfigureAwait(false);
            }
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
