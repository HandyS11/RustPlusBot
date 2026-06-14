using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Registry;

public sealed class WorkspaceRegistryTests
{
    [Fact]
    public void ChannelSpecs_AggregateAcrossProviders_OrderedByOrder()
    {
        var registry = new WorkspaceRegistry([new ProviderA(), new ProviderB()], []);

        var global = registry.GetChannelSpecs(WorkspaceScope.Global);
        Assert.Equal(["a", "b"], global.Select(s => s.Key));

        var perServer = registry.GetChannelSpecs(WorkspaceScope.PerServer);
        Assert.Equal(["info"], perServer.Select(s => s.Key));
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
