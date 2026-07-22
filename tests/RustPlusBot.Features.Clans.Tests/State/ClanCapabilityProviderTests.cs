using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Clans.State;
using RustPlusBot.Persistence.Clans;

namespace RustPlusBot.Features.Clans.Tests.State;

public sealed class ClanCapabilityProviderTests
{
    private const ulong Guild = 42UL;
    private static readonly Guid Server = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly FakeClock _clock = new(DateTimeOffset.UnixEpoch);

    private readonly IClanStore _store = Substitute.For<IClanStore>();

    [Fact]
    public async Task A_store_failure_faults_instead_of_answering_false()
    {
        // A "false" here makes the workspace reconciler DELETE #clanchat and #claninfo along with
        // all their history. On a transient store failure, failing loudly is the only safe answer.
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store down"));
        await using var host = Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));

        Assert.Equal("store down", ex.Message);
    }

    [Fact]
    public async Task A_failed_read_is_not_cached()
    {
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("store down"));
        await using var host = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));

        await _store.Received(2).HasClanAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_global_scope_is_unavailable_without_touching_the_store()
    {
        await using var host = Build();

        Assert.False(await host.Provider.IsAvailableAsync(Guild, null, CancellationToken.None));

        // Clans are per-server; the global scope never has clan channels, so not even a DI scope
        // should be opened for it.
        Assert.Equal(0, host.Factory.Created);
        await _store.DidNotReceive()
            .HasClanAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_answer_follows_the_stored_clan_row(bool hasClan)
    {
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(hasClan);
        await using var host = Build();

        Assert.Equal(hasClan, await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_call_inside_the_ttl_is_served_from_the_cache()
    {
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(true);
        await using var host = Build();

        Assert.True(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));
        _clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(4);
        Assert.True(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));

        await _store.Received(1).HasClanAsync(Guild, Server, Arg.Any<CancellationToken>());
        Assert.Equal(1, host.Factory.Created);
    }

    [Fact]
    public async Task A_call_after_the_ttl_re_reads_the_store()
    {
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(true);
        await using var host = Build();

        Assert.True(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));

        _clock.UtcNow = DateTimeOffset.UnixEpoch.AddSeconds(6);
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));
        await _store.Received(2).HasClanAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invalidate_drops_the_cached_answer()
    {
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(true);
        await using var host = Build();

        Assert.True(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));

        host.Provider.Invalidate(Guild, Server);
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(false);

        Assert.False(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));
    }

    [Fact]
    public async Task A_read_that_raced_an_invalidation_does_not_populate_the_cache()
    {
        await using var host = Build();

        // Simulates the TOCTOU window deterministically: this read queried the store BEFORE the
        // transition committed, and the invalidation lands while it is still in flight. Writing its
        // now-stale answer into the cache would make the transition reconcile create or delete
        // nothing, and WorkspaceHostedService only heals at startup and on ChannelDestroyed — so the
        // lost reconcile would persist until the next transition or a process restart.
        var raced = false;
        _store.HasClanAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (raced)
            {
                return false;
            }

            raced = true;
            host.Provider.Invalidate(Guild, Server);
            return true;
        });

        Assert.True(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));

        // Same instant, so the TTL cannot explain a re-read: the raced value must not have been cached.
        Assert.False(await host.Provider.IsAvailableAsync(Guild, Server, CancellationToken.None));
        await _store.Received(2).HasClanAsync(Guild, Server, Arg.Any<CancellationToken>());
    }

    private Host Build()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _store);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var factory = new CountingScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        return new Host(provider, factory, new ClanCapabilityProvider(factory, _clock));
    }

    private sealed class Host(
        ServiceProvider provider,
        CountingScopeFactory factory,
        ClanCapabilityProvider capabilityProvider) : IAsyncDisposable
    {
        public CountingScopeFactory Factory => factory;

        public ClanCapabilityProvider Provider => capabilityProvider;

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _created;

        public int Created => _created;

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref _created);
            return inner.CreateScope();
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
