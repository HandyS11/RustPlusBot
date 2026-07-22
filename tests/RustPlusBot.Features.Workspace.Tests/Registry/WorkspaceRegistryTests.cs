using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Registry;

public sealed class WorkspaceRegistryTests
{
    [Fact]
    public void ChannelSpecs_AggregateAcrossProviders_OrderedByOrder()
    {
        var registry = new WorkspaceRegistry([new ProviderA(), new ProviderB()], [], []);

        var global = registry.GetChannelSpecs(WorkspaceScope.Global);
        Assert.Equal(["a", "b"], global.Select(s => s.Key));

        var perServer = registry.GetChannelSpecs(WorkspaceScope.PerServer);
        Assert.Equal(["info"], perServer.Select(s => s.Key));
    }

    [Fact]
    public void Throws_when_a_channel_spec_names_a_capability_with_no_provider()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new WorkspaceRegistry([new GatedProvider()], [], []));

        Assert.Contains("ghost", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepts_a_gated_channel_whose_capability_provider_is_registered()
    {
        var registry = new WorkspaceRegistry([new GatedProvider()], [], [new StubCapability("ghost")]);

        Assert.Equal(["gated"], registry.GetChannelSpecs(WorkspaceScope.PerServer).Select(s => s.Key));
    }

    private sealed class GatedProvider : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() =>
        [
            new(WorkspaceScope.PerServer, "gated", "gated.name", ChannelPermissionProfile.ReadOnly, 0, "ghost"),
        ];
    }

    private sealed class StubCapability(string capability) : IWorkspaceCapabilityProvider
    {
        public string Capability { get; } = capability;

        public ValueTask<bool> IsAvailableAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class ProviderA : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() =>
        [
            new(WorkspaceScope.Global, "b", "b.name", ChannelPermissionProfile.ReadOnly, 1),
            new(WorkspaceScope.Global, "a", "a.name", ChannelPermissionProfile.ReadOnly, 0),
        ];
    }

    private sealed class ProviderB : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() =>
        [
            new(WorkspaceScope.PerServer, "info", "info.name", ChannelPermissionProfile.ReadOnly, 0),
        ];
    }
}
