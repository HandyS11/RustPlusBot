using System.Collections.Concurrent;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Fakes;

/// <summary>Deterministic in-memory <see cref="IWorkspaceGateway"/> for reconciler tests.</summary>
internal sealed class FakeWorkspaceGateway : IWorkspaceGateway
{
    private readonly ConcurrentDictionary<ulong, Category> _categories = new();
    private readonly ConcurrentDictionary<ulong, Channel> _channels = new();
    private readonly List<ulong> _deletedMessageIds = [];
    private readonly ConcurrentDictionary<ulong, Message> _messages = new();
    private readonly List<MessagePayload> _postedPayloads = [];
    private ulong _nextId = 1000;

    public IReadOnlyList<string> MissingPermissions { get; set; } = [];
    public int CreatedCategories { get; private set; }
    public int CreatedChannels { get; private set; }
    public int PostedMessages { get; private set; }
    public int EditedMessages { get; private set; }
    public IReadOnlyCollection<ulong> ChannelIds => [.. _channels.Keys];
    public IReadOnlyCollection<ulong> CategoryIds => [.. _categories.Keys];

    /// <summary>Snowflakes deleted via <see cref="DeleteMessageAsync"/>, in call order.</summary>
    public IReadOnlyList<ulong> DeletedMessageIds => _deletedMessageIds;

    /// <summary>Payloads posted via <see cref="PostMessageAsync"/>, in call order.</summary>
    public IReadOnlyList<MessagePayload> PostedPayloads => _postedPayloads;

    public bool CategoryExists(ulong guildId, ulong categoryId) => _categories.ContainsKey(categoryId);

    public Task<ulong?> FindCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var match = _categories.Values.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match is null ? (ulong?)null : match.Id);
    }

    public Task<ulong> CreateCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var id = NextId();
        _categories[id] = new Category(id, name);
        CreatedCategories++;
        return Task.FromResult(id);
    }

    public bool ChannelExists(ulong guildId, ulong channelId) => _channels.ContainsKey(channelId);

    public Task<ulong?> FindChannelAsync(ulong guildId,
        ulong categoryId,
        string name,
        CancellationToken cancellationToken)
    {
        var match = _channels.Values.FirstOrDefault(c =>
            c.CategoryId == categoryId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match is null ? (ulong?)null : match.Id);
    }

    public Task<ulong> CreateChannelAsync(ulong guildId,
        ulong categoryId,
        string name,
        ChannelPermissionProfile profile,
        CancellationToken cancellationToken)
    {
        var id = NextId();
        _channels[id] = new Channel(id, categoryId, name, profile);
        CreatedChannels++;
        return Task.FromResult(id);
    }

    public Task ApplyChannelSettingsAsync(ulong guildId,
        ulong channelId,
        ulong categoryId,
        string name,
        ChannelPermissionProfile profile,
        CancellationToken cancellationToken)
    {
        _channels[channelId] = new Channel(channelId, categoryId, name, profile);
        return Task.CompletedTask;
    }

    public Task<bool> MessageExistsAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_messages.ContainsKey(messageId));

    public Task<ulong> PostMessageAsync(ulong guildId,
        ulong channelId,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        var id = NextId();
        _messages[id] = new Message(id, channelId, payload);
        PostedMessages++;
        _postedPayloads.Add(payload);
        return Task.FromResult(id);
    }

    public Task EditMessageAsync(ulong guildId,
        ulong channelId,
        ulong messageId,
        MessagePayload payload,
        CancellationToken cancellationToken)
    {
        _messages[messageId] = new Message(messageId, channelId, payload);
        EditedMessages++;
        return Task.CompletedTask;
    }

    public Task DeleteMessageAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken)
    {
        _messages.TryRemove(messageId, out _);
        _deletedMessageIds.Add(messageId);
        return Task.CompletedTask;
    }

    public Task DeleteChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        _channels.TryRemove(channelId, out _);
        return Task.CompletedTask;
    }

    public Task DeleteCategoryAsync(ulong guildId, ulong categoryId, CancellationToken cancellationToken)
    {
        _categories.TryRemove(categoryId, out _);
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> GetMissingBotPermissions(ulong guildId) => MissingPermissions;

    private ulong NextId() => Interlocked.Increment(ref _nextId);

    public void ExternallyDeleteChannel(ulong channelId) => _channels.TryRemove(channelId, out _);
    public void ExternallyDeleteCategory(ulong categoryId) => _categories.TryRemove(categoryId, out _);
    public void ExternallyDeleteMessage(ulong messageId) => _messages.TryRemove(messageId, out _);

    public MessagePayload? GetMessagePayload(ulong messageId) =>
        _messages.TryGetValue(messageId, out var m) ? m.Payload : null;

    public ChannelPermissionProfile ProfileOf(ulong channelId) => _channels[channelId].Profile;

    private sealed record Category(ulong Id, string Name);

    private sealed record Channel(ulong Id, ulong CategoryId, string Name, ChannelPermissionProfile Profile);

    private sealed record Message(ulong Id, ulong ChannelId, MessagePayload Payload);
}
