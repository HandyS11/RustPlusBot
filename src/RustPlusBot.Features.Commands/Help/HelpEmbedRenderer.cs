using System.Globalization;
using Discord;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Help;

/// <summary>Builds the <c>/help</c> embed from the catalog, the server prefix, and the guild culture.</summary>
/// <param name="localizer">Resolves the title, group headings, and per-command descriptions.</param>
internal sealed class HelpEmbedRenderer(ILocalizer localizer)
{
    private static readonly (CommandGroup Group, string HeadingKey)[] GroupOrder =
    [
        (CommandGroup.Control, "help.group.control"),
        (CommandGroup.Server, "help.group.server"),
        (CommandGroup.TeamIntel, "help.group.teamintel"),
        (CommandGroup.Bot, "help.group.bot"),
    ];

    /// <summary>Renders the help embed.</summary>
    /// <param name="prefix">The in-game command prefix to display (e.g. "!").</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="showPrefixNote">True to add the multi-server prefix note to the description.</param>
    /// <returns>The built embed.</returns>
    public Embed Render(string prefix, string culture, bool showPrefixNote)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("help.title", culture))
            .WithColor(Color.Blue);

        if (showPrefixNote)
        {
            embed.WithDescription(localizer.Get("help.prefixnote", culture));
        }

        foreach (var (group, headingKey) in GroupOrder)
        {
            var lines = CommandHelpCatalog.InGame
                .Where(e => e.Group == group)
                .Select(e => string.Create(CultureInfo.InvariantCulture,
                    $"`{prefix}{e.Name}` — {localizer.Get(e.DescriptionKey, culture)}"))
                .ToList();
            if (lines.Count > 0)
            {
                embed.AddField(localizer.Get(headingKey, culture), string.Join('\n', lines));
            }
        }

        var slashLines = CommandHelpCatalog.Slash
            .Select(e => string.Create(CultureInfo.InvariantCulture,
                $"`/{e.Name}` — {localizer.Get(e.DescriptionKey, culture)}"))
            .ToList();
        embed.AddField(localizer.Get("help.group.slash", culture), string.Join('\n', slashLines));

        return embed.Build();
    }
}
