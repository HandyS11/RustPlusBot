using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Tests.Fakes;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>Builds a WorkspaceReconciler wired with fakes for tests.</summary>
internal sealed class ReconcilerHarness
{
    private readonly List<IWorkspaceCapabilityProvider> _capabilityProviders = [];
    private readonly List<IChannelSpecProvider> _channelProviders = [];
    private readonly List<IMessageSpecProvider> _messageProviders = [];
    private readonly List<IMessageRenderer> _renderers = [];
    public FakeWorkspaceGateway Gateway { get; } = new();
    public FakeWorkspaceStore Store { get; } = new();
    public IServerService Servers { get; } = Substitute.For<IServerService>();

    public ReconcilerHarness WithChannel(WorkspaceScope scope,
        string key,
        string nameKey,
        int order = 0,
        string? capability = null)
    {
        _channelProviders.Add(new StubChannelProvider([
            new ChannelSpec(scope, key, nameKey, ChannelPermissionProfile.ReadOnly, order, capability)
        ]));
        return this;
    }

    public ReconcilerHarness WithMessage(WorkspaceScope scope,
        string key,
        string channelKey,
        string text)
    {
        _messageProviders.Add(new StubMessageProvider([new MessageSpec(scope, key, channelKey)]));
        _renderers.Add(new StubRenderer(key, text));
        return this;
    }

    public ReconcilerHarness WithCapability(string capability, bool available)
    {
        _capabilityProviders.Add(new StubCapabilityProvider(capability, available));
        return this;
    }

    public WorkspaceReconciler Build()
    {
        var registry = new WorkspaceRegistry(_channelProviders, _messageProviders, _capabilityProviders);
        return new WorkspaceReconciler(
            new WorkspaceBackends(registry, Gateway, Store), _renderers, Servers,
            new ResxLocalizer(), new ProvisioningLock(),
            NullLogger<WorkspaceReconciler>.Instance);
    }

    private sealed class StubChannelProvider(IEnumerable<ChannelSpec> specs) : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() => specs;
    }

    private sealed class StubMessageProvider(IEnumerable<MessageSpec> specs) : IMessageSpecProvider
    {
        public IEnumerable<MessageSpec> GetMessageSpecs() => specs;
    }

    private sealed class StubRenderer(string key, string text) : IMessageRenderer
    {
        public string MessageKey { get; } = key;

        public ValueTask<MessagePayload>
            RenderAsync(MessageRenderContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MessagePayload(text, null, null));
    }

    private sealed class StubCapabilityProvider(string capability, bool available) : IWorkspaceCapabilityProvider
    {
        public string Capability { get; } = capability;

        public ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(available);
    }
}

internal sealed class ReconcilerBuilderReusing(ReconcilerHarness source)
{
    private readonly List<IWorkspaceCapabilityProvider> _capabilityProviders = [];
    private readonly List<IChannelSpecProvider> _channelProviders = [];
    private readonly List<IMessageSpecProvider> _messageProviders = [];
    private readonly List<IMessageRenderer> _renderers = [];

    public ReconcilerBuilderReusing WithChannel(WorkspaceScope scope,
        string key,
        string nameKey,
        int order = 0,
        string? capability = null)
    {
        _channelProviders.Add(new ListChannelProvider([
            new ChannelSpec(scope, key, nameKey, ChannelPermissionProfile.ReadOnly, order, capability)
        ]));
        return this;
    }

    public ReconcilerBuilderReusing WithMessage(WorkspaceScope scope,
        string key,
        string channelKey,
        string text)
    {
        _messageProviders.Add(new ListMessageProvider([new MessageSpec(scope, key, channelKey)]));
        _renderers.Add(new ListMessageRenderer(key, text));
        return this;
    }

    public ReconcilerBuilderReusing WithCapability(string capability, bool available)
    {
        _capabilityProviders.Add(new ListCapabilityProvider(capability, available));
        return this;
    }

    public WorkspaceReconciler Build() => new(
        new WorkspaceBackends(new WorkspaceRegistry(_channelProviders, _messageProviders, _capabilityProviders),
            source.Gateway,
            source.Store),
        _renderers, source.Servers,
        new ResxLocalizer(), new ProvisioningLock(),
        NullLogger<WorkspaceReconciler>.Instance);

    private sealed class ListChannelProvider(IEnumerable<ChannelSpec> specs) : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() => specs;
    }

    private sealed class ListMessageProvider(IEnumerable<MessageSpec> specs) : IMessageSpecProvider
    {
        public IEnumerable<MessageSpec> GetMessageSpecs() => specs;
    }

    private sealed class ListMessageRenderer(string key, string text) : IMessageRenderer
    {
        public string MessageKey { get; } = key;

        public ValueTask<MessagePayload>
            RenderAsync(MessageRenderContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MessagePayload(text, null, null));
    }

    private sealed class ListCapabilityProvider(string capability, bool available) : IWorkspaceCapabilityProvider
    {
        public string Capability { get; } = capability;

        public ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(available);
    }
}
