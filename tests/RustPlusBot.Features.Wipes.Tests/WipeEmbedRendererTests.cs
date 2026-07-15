using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Unit tests for <see cref="WipeEmbedRenderer"/>.</summary>
public sealed class WipeEmbedRendererTests
{
    private static readonly DateTimeOffset WipedAt = new(2026, 7, 2, 18, 0, 0, TimeSpan.Zero);

    private static ServerWipedEvent Event(DateTimeOffset? newWipe = null) =>
        new(10UL, Guid.NewGuid(), WipedAt.AddDays(-7), newWipe ?? WipedAt, 999u, 4250u);

    [Fact]
    public void Render_includes_title_body_and_fields()
    {
        var renderer = new WipeEmbedRenderer(new ResxLocalizer());

        var embed = renderer.Render(Event(), "en");

        Assert.Equal("🧹 Server wiped", embed.Title);
        Assert.False(string.IsNullOrEmpty(embed.Description));
        Assert.Contains(embed.Fields, f => f.Value == $"<t:{WipedAt.ToUnixTimeSeconds()}:R>");
        Assert.Contains(embed.Fields, f => f.Value == "4250");
        Assert.Contains(embed.Fields, f => f.Value == "999");
    }

    [Fact]
    public void Render_omits_wiped_at_field_when_wipe_time_unknown()
    {
        var renderer = new WipeEmbedRenderer(new ResxLocalizer());

        var embed = renderer.Render(Event() with
        {
            NewWipeTimeUtc = null
        }, "en");

        Assert.Equal(2, embed.Fields.Length);
    }

    [Fact]
    public void Render_localizes_to_french()
    {
        var renderer = new WipeEmbedRenderer(new ResxLocalizer());

        var en = renderer.Render(Event(), "en");
        var fr = renderer.Render(Event(), "fr");

        Assert.NotEqual(en.Description, fr.Description);
    }
}
