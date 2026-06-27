using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Help;

public sealed class CommandHelpCatalogTests
{
    /// <summary>The 18 registered in-game handler names (see AddCommands / CommandRegistrationTests).</summary>
    private static readonly string[] HandlerNames =
    [
        "mute", "unmute", "uptime", "pop", "wipe", "time",
        "online", "offline", "team", "steamid", "alive", "afk", "prox",
        "item", "recycle", "craft", "research", "decay",
    ];

    [Fact]
    public void EveryRegisteredHandlerHasAnInGameEntry()
    {
        var entryNames = CommandHelpCatalog.InGame.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in HandlerNames)
        {
            Assert.Contains(name, entryNames);
        }
    }

    [Fact]
    public void InGameCatalogHasNoEntryWithoutAHandler()
    {
        var handlerNames = HandlerNames.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in CommandHelpCatalog.InGame)
        {
            Assert.Contains(entry.Name, handlerNames);
        }
    }

    [Fact]
    public void EveryDescriptionKeyResolvesInEnglishAndFrench()
    {
        var loc = new ResxLocalizer();
        foreach (var entry in CommandHelpCatalog.InGame.Concat(CommandHelpCatalog.Slash))
        {
            Assert.NotEqual(entry.DescriptionKey, loc.Get(entry.DescriptionKey, "en"));
            Assert.NotEqual(entry.DescriptionKey, loc.Get(entry.DescriptionKey, "fr"));
        }
    }

    [Fact]
    public void EveryInGameGroupIsCoveredByHelpEmbedRenderer()
    {
        var coveredGroups = HelpEmbedRenderer.GroupOrder.Select(g => g.Group).ToHashSet();
        foreach (var group in CommandHelpCatalog.InGame.Select(e => e.Group).Distinct())
        {
            Assert.Contains(group, coveredGroups);
        }
    }
}
