using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Commands.Servers;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Tests.Servers;

public sealed class ServerResolverTests
{
    private static readonly CommandLocalizer Loc = new(CommandLocalizationCatalog.Default);

    private static RustServer Server(Guid id, string name) => new()
    {
        Id = id, Name = name, GuildId = 1UL
    };

    private static ServerResolver Build(params RustServer[] servers)
    {
        var svc = Substitute.For<IServerService>();
        svc.ListAsync(1UL, Arg.Any<CancellationToken>()).Returns(servers);
        return new ServerResolver(svc, Loc);
    }

    [Fact]
    public async Task ReturnsError_WhenNoServers()
    {
        var r = await Build().ResolveAsync(1UL, null, "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("No server is set up yet.", r.ErrorMessage);
    }

    [Fact]
    public async Task DefaultsToSingleServer_WhenNoArg()
    {
        var id = Guid.NewGuid();
        var r = await Build(Server(id, "Main")).ResolveAsync(1UL, null, "en", CancellationToken.None);
        Assert.Equal(id, r.ServerId);
        Assert.Equal("Main", r.Name);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public async Task ReturnsSpecifyError_WhenManyAndNoArg()
    {
        var r = await Build(Server(Guid.NewGuid(), "A"), Server(Guid.NewGuid(), "B"))
            .ResolveAsync(1UL, null, "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("Multiple servers are set up — choose one with the server option.", r.ErrorMessage);
    }

    [Fact]
    public async Task ResolvesExplicitArg_WhenMany()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var r = await Build(Server(a, "A"), Server(b, "B"))
            .ResolveAsync(1UL, b.ToString(), "en", CancellationToken.None);
        Assert.Equal(b, r.ServerId);
        Assert.Equal("B", r.Name);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public async Task ReturnsUnknownError_WhenArgNotRegistered()
    {
        var r = await Build(Server(Guid.NewGuid(), "A"))
            .ResolveAsync(1UL, Guid.NewGuid().ToString(), "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("That server isn't set up.", r.ErrorMessage);
    }

    [Fact]
    public async Task ReturnsUnknownError_WhenArgUnparseable()
    {
        var r = await Build(Server(Guid.NewGuid(), "A"), Server(Guid.NewGuid(), "B"))
            .ResolveAsync(1UL, "not-a-guid", "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("That server isn't set up.", r.ErrorMessage);
    }
}
