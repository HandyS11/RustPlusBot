namespace RustPlusBot.Features.Commands.Help;

/// <summary>One entry in the help listing.</summary>
/// <param name="Name">The command name without prefix (in-game) or slash command name.</param>
/// <param name="Group">The display group.</param>
/// <param name="DescriptionKey">The localization key for the description.</param>
internal sealed record CommandHelpEntry(string Name, CommandGroup Group, string DescriptionKey);

/// <summary>The curated help manifest. Tested against the live handler registry to catch drift.</summary>
internal static class CommandHelpCatalog
{
    /// <summary>The in-game <c>!commands</c>, in display order.</summary>
    public static IReadOnlyList<CommandHelpEntry> InGame { get; } =
    [
        new("mute", CommandGroup.Control, "help.mute"),
        new("unmute", CommandGroup.Control, "help.unmute"),
        new("pop", CommandGroup.Server, "help.pop"),
        new("time", CommandGroup.Server, "help.time"),
        new("wipe", CommandGroup.Server, "help.wipe"),
        new("online", CommandGroup.TeamIntel, "help.online"),
        new("offline", CommandGroup.TeamIntel, "help.offline"),
        new("team", CommandGroup.TeamIntel, "help.team"),
        new("steamid", CommandGroup.TeamIntel, "help.steamid"),
        new("alive", CommandGroup.TeamIntel, "help.alive"),
        new("afk", CommandGroup.TeamIntel, "help.afk"),
        new("prox", CommandGroup.TeamIntel, "help.prox"),
        new("uptime", CommandGroup.Bot, "help.uptime"),
        new("item", CommandGroup.ItemDb, "help.item"),
        new("recycle", CommandGroup.ItemDb, "help.recycle"),
        new("craft", CommandGroup.ItemDb, "help.craft"),
        new("research", CommandGroup.ItemDb, "help.research"),
        new("decay", CommandGroup.ItemDb, "help.decay"),
        new("upkeep", CommandGroup.ItemDb, "help.upkeep"),
        new("durability", CommandGroup.ItemDb, "help.durability"),
    ];

    /// <summary>The Discord slash commands, in display order.</summary>
    public static IReadOnlyList<CommandHelpEntry> Slash { get; } =
    [
        new("help", CommandGroup.Bot, "help.slash.help"),
        new("uptime", CommandGroup.Bot, "help.slash.uptime"),
        new("leader", CommandGroup.Bot, "help.slash.leader"),
        new("item", CommandGroup.ItemDb, "help.slash.item"),
        new("recycle", CommandGroup.ItemDb, "help.slash.recycle"),
        new("craft", CommandGroup.ItemDb, "help.slash.craft"),
        new("research", CommandGroup.ItemDb, "help.slash.research"),
        new("decay", CommandGroup.ItemDb, "help.slash.decay"),
        new("upkeep", CommandGroup.ItemDb, "help.slash.upkeep"),
    ];
}
