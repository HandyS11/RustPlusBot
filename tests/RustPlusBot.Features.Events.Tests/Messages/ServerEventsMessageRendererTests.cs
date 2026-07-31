using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Events.Messages;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Events.Tests.Messages;

public sealed class ServerEventsMessageRendererTests
{
    private static readonly ResxLocalizer Loc = new();
    private static readonly Guid ServerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static ServerEventsMessageRenderer Build(
        IEventState events,
        IRigState rigs,
        ConnectionStatus status = ConnectionStatus.Connected)
    {
        var mapSettings = Substitute.For<IMapSettingsStore>();
        mapSettings.GetAsync(1, ServerId, Arg.Any<CancellationToken>()).Returns(MapLayerSettings.AllOn);
        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, ServerId, Arg.Any<CancellationToken>())
            .Returns(new ConnectionState
            {
                GuildId = 1, RustServerId = ServerId, Status = status
            });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ServerEventsMessageRenderer(events, rigs, mapSettings, connections, clock, Loc);
    }

    [Fact]
    public async Task Renders_all_five_rows()
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        Assert.NotNull(payload.Embed);
        Assert.Equal(5, payload.Embed.Fields.Length);
    }

    [Fact]
    public async Task Absent_marker_renders_not_out()
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var cargo = payload.Embed!.Fields.Single(f => f.Name.Contains("Cargo", StringComparison.Ordinal));
        Assert.Equal("Not out", cargo.Value);
    }

    [Fact]
    public async Task Present_marker_renders_grid_and_age()
    {
        // MapDimensions ctor is (Width, Height, OceanMargin, WorldSize) in this repo, not
        // (WorldSize, ...) — WorldSize is the last argument and is what GridReference.From reads.
        var dims = new MapDimensions(4000, 4000, 0, 4000);
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        events.GetActiveMarkers(1, ServerId, MarkerKind.CargoShip)
            .Returns([
                new ActiveMarker(9UL, MarkerKind.CargoShip, 2000f, 2000f, dims, Now.AddMinutes(-12), [], null),
            ]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var cargo = payload.Embed!.Fields.Single(f => f.Name.Contains("Cargo", StringComparison.Ordinal));
        Assert.StartsWith("Out ·", cargo.Value, StringComparison.Ordinal);
        Assert.Contains("12m ago", cargo.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RigStatus.Online, "Online")]
    [InlineData(RigStatus.Active, "Crate unlocking · 4m left")]
    [InlineData(RigStatus.Offline, "Looted · respawns in 4m")]
    public async Task Rig_phases_render_distinctly(RigStatus status, string expected)
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, RigKind.Small).Returns(new RigState(status, TimeSpan.FromMinutes(4)));
        rigs.Get(1, ServerId, RigKind.Large).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        var small = payload.Embed!.Fields.Single(f => f.Name.Contains("Small", StringComparison.Ordinal));
        Assert.Equal(expected, small.Value);
    }

    [Fact]
    public async Task No_server_scope_renders_empty_payload()
    {
        var events = Substitute.For<IEventState>();
        var rigs = Substitute.For<IRigState>();

        var payload = await Build(events, rigs).RenderAsync(new MessageRenderContext(1, null, "en"), default);

        Assert.Null(payload.Embed);
        Assert.Null(payload.Text);
        Assert.Null(payload.Components);
    }

    [Fact]
    public async Task Disconnected_says_so_instead_of_reporting_stale_state()
    {
        var events = Substitute.For<IEventState>();
        events.GetActiveMarkers(1, ServerId, Arg.Any<MarkerKind>()).Returns([]);
        var rigs = Substitute.For<IRigState>();
        rigs.Get(1, ServerId, Arg.Any<RigKind>()).Returns(new RigState(RigStatus.Online, null));

        var payload = await Build(events, rigs, ConnectionStatus.Unreachable)
            .RenderAsync(new MessageRenderContext(1, ServerId, "en"), default);

        // In-process marker and rig state is cleared on disconnect, so an unqualified
        // "Not out / Online" wall would read as a confident live report.
        Assert.Equal("Not connected to the server.", payload.Embed!.Description);
    }
}
