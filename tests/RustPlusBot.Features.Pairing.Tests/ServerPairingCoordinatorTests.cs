using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Posting;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class ServerPairingCoordinatorTests
{
    private static readonly Guid FpServer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ICredentialProtector PassThrough()
    {
        var p = Substitute.For<ICredentialProtector>();
        p.Protect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        return p;
    }

    private static PairingNotification ServerPairing(string ip = "1.2.3.4", int port = 28015, ulong steam = 7UL) =>
        new(PairingKind.Server, "Rustopia", ip, port, steam, "ptoken", FacepunchServerId: FpServer, EntityId: 0UL);

    private static Harness Create(ulong? channelId = 777UL)
    {
        var (context, connection) = TestDb.Create();

        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped<IServerService>(_ => new ServerService(context));
        services.AddScoped<ICredentialStore>(_ => new CredentialStore(context, PassThrough()));
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();

        var locator = Substitute.For<ISetupChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(channelId);

        var poster = Substitute.For<ISetupChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var notifier = Substitute.For<IOwnerNotifier>();
        var bus = Substitute.For<IEventBus>();
        var coordinator = new ServerPairingCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(), locator, poster,
            new ServerPairingPromptRenderer(new ResxLocalizer()), notifier, bus,
            NullLogger<ServerPairingCoordinator>.Instance);

        return new Harness(coordinator, context, connection, locator, poster, notifier, bus);
    }

    [Fact]
    public async Task Detected_posts_prompt_holds_pending_and_persists_nothing()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;

        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, null, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.True(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
        Assert.Empty(await h.Context.RustServers.ToListAsync());
        Assert.Empty(await h.Context.PlayerCredentials.ToListAsync());
        await h.Bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detected_again_while_pending_refreshes_without_reposting()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;

        await h.Coordinator.HandleDetectedAsync(10UL, 1UL, ServerPairing(steam: 1UL), CancellationToken.None);
        await h.Coordinator.HandleDetectedAsync(10UL, 2UL, ServerPairing(steam: 2UL), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());

        // Accepting proves the refreshed pairing (owner 2) won.
        await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);
        var server = await h.Context.RustServers.SingleAsync();
        Assert.Equal(2UL, server.AddedByUserId);
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(2UL, credential.OwnerUserId);
    }

    [Fact]
    public async Task Detected_without_setup_channel_notifies_owner_and_drops()
    {
        var h = Create(channelId: null);
        await using var _ = h.Context;
        await using var __ = h.Connection;

        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        await h.Notifier.Received(1).NotifySetupChannelMissingAsync(10UL, 99UL, Arg.Any<CancellationToken>());
        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_persists_publishes_event_once_and_edits_prompt()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.Added, outcome);
        var server = await h.Context.RustServers.SingleAsync();
        Assert.Equal("Rustopia", server.Name);
        Assert.Equal(FpServer, server.FacepunchServerId);
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(server.Id, credential.RustServerId);
        Assert.Equal(CredentialStatus.Active, credential.Status);
        await h.Bus.Received(1).PublishAsync(
            Arg.Is<ServerRegisteredEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id),
            Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
    }

    [Fact]
    public async Task Accept_when_server_already_exists_upserts_credential_without_event()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        // Another path created the same endpoint while the prompt sat unanswered.
        await new ServerService(h.Context).AddAsync(10UL, 50UL, "Rustopia", "1.2.3.4", 28015);

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.AlreadyAdded, outcome);
        Assert.Single(await h.Context.RustServers.ToListAsync());
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(CredentialStatus.Standby, credential.Status);
        await h.Bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_without_pending_returns_expired_and_persists_nothing()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.Expired, outcome);
        Assert.Empty(await h.Context.RustServers.ToListAsync());
        await h.Bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Concurrent_detections_for_same_endpoint_post_single_prompt()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        var gate = new TaskCompletionSource<ulong?>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);

        var first = h.Coordinator.HandleDetectedAsync(10UL, 1UL, ServerPairing(steam: 1UL), CancellationToken.None);
        var second = h.Coordinator.HandleDetectedAsync(10UL, 2UL, ServerPairing(steam: 2UL), CancellationToken.None);
        gate.SetResult(900UL);
        await Task.WhenAll(first, second);

        await h.Poster.Received(1).EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
        Assert.True(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
    }

    [Fact]
    public async Task Detected_with_failed_prompt_post_drops_pending_and_repair_retries()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        h.Poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
        Assert.Empty(await h.Context.RustServers.ToListAsync());

        h.Poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        await h.Poster.Received(2).EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
        Assert.True(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
    }

    [Fact]
    public async Task Accept_without_setup_channel_still_persists_and_skips_edit()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        h.Locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns((ulong?)null);

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.Added, outcome);
        var server = await h.Context.RustServers.SingleAsync();
        Assert.Equal("Rustopia", server.Name);
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(server.Id, credential.RustServerId);
        Assert.Equal(CredentialStatus.Active, credential.Status);
        await h.Bus.Received(1).PublishAsync(
            Arg.Is<ServerRegisteredEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id),
            Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dismiss_clears_pending_once()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        Assert.True(h.Coordinator.TryDismiss(10UL, "1.2.3.4", 28015));
        Assert.False(h.Coordinator.TryDismiss(10UL, "1.2.3.4", 28015));
        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
    }

    private sealed record Harness(
        ServerPairingCoordinator Coordinator,
        BotDbContext Context,
        Microsoft.Data.Sqlite.SqliteConnection Connection,
        ISetupChannelLocator Locator,
        ISetupChannelPoster Poster,
        IOwnerNotifier Notifier,
        IEventBus Bus);
}
