using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Tests.Help;

public sealed class HelpEmbedRendererTests
{
    private static readonly HelpEmbedRenderer Renderer =
        new(new CommandLocalizer(CommandLocalizationCatalog.Default));

    [Fact]
    public void Render_PrefixesInGameCommands()
    {
        var embed = Renderer.Render("$", "en", showPrefixNote: false);

        var body = string.Concat(embed.Fields.Select(f => f.Value));
        Assert.Contains("$pop", body, StringComparison.Ordinal);
        Assert.Contains("$online", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesSlashCommandsGroup()
    {
        var embed = Renderer.Render("!", "en", showPrefixNote: false);

        var body = string.Concat(embed.Fields.Select(f => f.Value));
        Assert.Contains("/leader", body, StringComparison.Ordinal);
        Assert.Contains("/uptime", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AddsPrefixNote_WhenRequested()
    {
        var withNote = Renderer.Render("!", "en", showPrefixNote: true);
        var withoutNote = Renderer.Render("!", "en", showPrefixNote: false);

        Assert.NotNull(withNote.Description);
        Assert.Contains("per server", withNote.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(withoutNote.Description));
    }

    [Fact]
    public void Render_LocalizesToFrench()
    {
        var embed = Renderer.Render("!", "fr", showPrefixNote: false);

        var body = string.Concat(embed.Fields.Select(f => f.Value));
        Assert.Contains("Afficher la population", body, StringComparison.Ordinal);
    }
}
