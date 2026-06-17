using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Rendering;

namespace RustPlusBot.Features.Events.Tests.Rendering;

public sealed class EventEmbedRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static EventEmbedRenderer Build() =>
        new(new EventLocalizer(EventLocalizationCatalog.Default));

    [Fact]
    public void Cargo_entered_renders_english_with_grid()
    {
        var dims = new MapDimensions(4000u, 4000u, 500);
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
}
