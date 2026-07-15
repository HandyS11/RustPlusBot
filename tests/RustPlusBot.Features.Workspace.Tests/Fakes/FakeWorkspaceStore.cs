using System.Collections.Concurrent;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Fakes;

/// <summary>In-memory <see cref="IWorkspaceStore"/> for reconciler unit tests.</summary>
internal sealed class FakeWorkspaceStore : IWorkspaceStore
{
    private readonly ConcurrentDictionary<string, ProvisionedCategory> _categories = new();

    /// <summary>Key: scope|channelKey.</summary>
    private readonly ConcurrentDictionary<string, ProvisionedChannel> _channels = new();

    private readonly ConcurrentDictionary<ulong, string> _cultures = new();

    /// <summary>Key: scope|messageKey.</summary>
    private readonly ConcurrentDictionary<string, ProvisionedMessage> _messages = new();

    private readonly ConcurrentDictionary<ulong, bool> _pingEveryoneOnWipe = new();

    public Task<ProvisionedCategory?> GetCategoryAsync(ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.GetValueOrDefault(Scope(guildId, serverId)));

    public Task SaveCategoryAsync(ProvisionedCategory category, CancellationToken cancellationToken = default)
    {
        _categories[Scope(category.GuildId, category.RustServerId)] = category;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProvisionedChannel>> GetChannelsAsync(ulong guildId,
        Guid? serverId,
        CancellationToken cancellationToken = default)
    {
        var prefix = Scope(guildId, serverId) + "|";
        IReadOnlyList<ProvisionedChannel> list =
        [
            .. _channels
                .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => kv.Value)
        ];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ProvisionedChannel>> GetChannelsByKeyAsync(
        string channelKey,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProvisionedChannel> result =
            [.. _channels.Values.Where(c => c.ChannelKey == channelKey)];
        return Task.FromResult(result);
    }

    public Task SaveChannelAsync(ProvisionedChannel channel, CancellationToken cancellationToken = default)
    {
        _channels[$"{Scope(channel.GuildId, channel.RustServerId)}|{channel.ChannelKey}"] = channel;
        return Task.CompletedTask;
    }

    public Task<ProvisionedMessage?> GetMessageAsync(ulong guildId,
        Guid? serverId,
        string messageKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_messages.GetValueOrDefault($"{Scope(guildId, serverId)}|{messageKey}"));

    public Task SaveMessageAsync(ProvisionedMessage message, CancellationToken cancellationToken = default)
    {
        _messages[$"{Scope(message.GuildId, message.RustServerId)}|{message.MessageKey}"] = message;
        return Task.CompletedTask;
    }

    public Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cultures.GetValueOrDefault(guildId, "en"));

    public Task SetCultureAsync(ulong guildId, string culture, CancellationToken cancellationToken = default)
    {
        _cultures[guildId] = culture;
        return Task.CompletedTask;
    }

    public Task<bool> GetPingEveryoneOnWipeAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pingEveryoneOnWipe.GetValueOrDefault(guildId, false));

    public Task SetPingEveryoneOnWipeAsync(ulong guildId, bool enabled, CancellationToken cancellationToken = default)
    {
        _pingEveryoneOnWipe[guildId] = enabled;
        return Task.CompletedTask;
    }

    public Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default)
    {
        _categories.TryRemove(Scope(guildId, serverId), out _);
        var prefix = Scope(guildId, serverId) + "|";
        foreach (var key in _channels.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _channels.TryRemove(key, out _);
        }

        foreach (var key in _messages.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _messages.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProvisionedCategory>> GetAllCategoriesAsync(ulong guildId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProvisionedCategory> list = [.. _categories.Values.Where(c => c.GuildId == guildId)];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ulong>> GetProvisionedGuildIdsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ulong> list = [.. _categories.Values.Select(c => c.GuildId).Distinct()];
        return Task.FromResult(list);
    }

    private static string Scope(ulong g, Guid? s) => $"{g}:{s?.ToString() ?? "global"}";
}
