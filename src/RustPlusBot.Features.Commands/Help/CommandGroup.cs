namespace RustPlusBot.Features.Commands.Help;

/// <summary>The display grouping for a command in the <c>/help</c> listing.</summary>
internal enum CommandGroup
{
    /// <summary>Bot control commands (mute/unmute).</summary>
    Control = 0,

    /// <summary>Server-state commands (pop/time/wipe).</summary>
    Server = 1,

    /// <summary>Team-intel commands (online/offline/team/steamid/alive/prox).</summary>
    TeamIntel = 2,

    /// <summary>Bot-meta commands (uptime).</summary>
    Bot = 3,

    /// <summary>Item-database commands (item/recycle/craft/research).</summary>
    ItemDb = 4,
}
