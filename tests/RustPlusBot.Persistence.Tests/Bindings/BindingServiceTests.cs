using RustPlusBot.Domain.Guilds;
using RustPlusBot.Persistence.Bindings;

namespace RustPlusBot.Persistence.Tests.Bindings;

public sealed class BindingServiceTests
{
    [Fact]
    public async Task BindAsync_StoresChannelForFeature()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new BindingService(context);

        await service.BindAsync(10UL, BoundFeature.Chat, 555UL);

        Assert.Equal(555UL, await service.GetBoundChannelAsync(10UL, BoundFeature.Chat));
        Assert.Null(await service.GetBoundChannelAsync(10UL, BoundFeature.Events));
    }

    [Fact]
    public async Task BindAsync_RebindingFeature_ReplacesChannel()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new BindingService(context);

        await service.BindAsync(10UL, BoundFeature.Chat, 555UL);
        await service.BindAsync(10UL, BoundFeature.Chat, 777UL);

        Assert.Equal(777UL, await service.GetBoundChannelAsync(10UL, BoundFeature.Chat));
    }
}
