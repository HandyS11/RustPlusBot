using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Tests.Rendering;

public sealed class EventEmbedRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static EventEmbedRenderer Build() =>
        new(new ResxLocalizer());

    [Fact]
    public void Cargo_entered_renders_english_with_grid()
    {
        var dims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);
        var embed = Build().Render(new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f, dims, Now), "en");

        Assert.NotNull(embed.Description);
        Assert.Contains("Cargo Ship entered", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Chinook_spawned_renders_french()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.ChinookSpawned, 0f, 0f, null, Now), "fr");
        Assert.Contains("Chinook apparu", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_dimensions_render_raw_coordinates()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliEntered, 1234f, 5678f, null, Now), "en");
        Assert.Contains("(1234, 5678)", embed.Description, StringComparison.Ordinal);
    }

    private static readonly MapDimensions Dims4000 = new(4000u, 4000u, 500, WorldSize: 4000u);

    [Fact]
    public void Heli_crashed_renders_a_grid_cell()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now), "en");

        Assert.Equal("🚁 Patrol Helicopter probably crashed at N13", embed.Description);
    }

    [Fact]
    public void Heli_crashed_renders_french()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now), "fr");

        Assert.Contains("probablement abattu en", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Heli_left_renders_a_direction()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliLeft, 100f, 2000f, Dims4000, Now), "en");

        Assert.Equal("🚁 Patrol Helicopter left the map to the west", embed.Description);
    }

    [Fact]
    public void Cargo_entered_off_map_renders_a_direction_not_a_clamped_cell()
    {
        // The bug being fixed: an ocean spawn outside the world used to report the clamped edge cell.
        var embed = Build().Render(new RustMapEvent(MapEventKind.CargoEntered, 4500f, 4500f, Dims4000, Now), "en");

        Assert.Equal("🚢 Cargo Ship entered from the north-east", embed.Description);
    }

    [Fact]
    public void Cargo_entered_on_map_still_renders_a_cell()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f, Dims4000, Now), "en");

        Assert.Equal("🚢 Cargo Ship entered at A0", embed.Description);
    }

    [Fact]
    public void Left_without_dimensions_keeps_the_raw_coordinate_wording()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliLeft, 1234f, 5678f, null, Now), "en");

        Assert.Equal("🚁 Patrol Helicopter left ((1234, 5678))", embed.Description);
    }

    [Fact]
    public void Lines_follow_the_same_direction_split()
    {
        var renderer = Build();

        Assert.Equal("Patrol Helicopter probably crashed at N13",
            renderer.RenderLine(new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now), "en"));
        Assert.Equal("Chinook spawned to the south-west",
            renderer.RenderLine(new RustMapEvent(MapEventKind.ChinookSpawned, -100f, -100f, Dims4000, Now), "en"));
        Assert.Equal("Cargo Ship left the map to the north-east",
            renderer.RenderLine(new RustMapEvent(MapEventKind.CargoLeft, 4500f, 4500f, Dims4000, Now), "en"));
    }
}
