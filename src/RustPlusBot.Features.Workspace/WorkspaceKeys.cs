namespace RustPlusBot.Features.Workspace;

/// <summary>Stable channel keys persisted as <c>ProvisionedChannel.ChannelKey</c>.</summary>
internal static class WorkspaceChannelKeys
{
    /// <summary>Key for the #information channel.</summary>
    public const string Information = "information";

    /// <summary>Key for the #setup channel.</summary>
    public const string Setup = "setup";

    /// <summary>Key for the #settings channel.</summary>
    public const string Settings = "settings";

    /// <summary>Key for the per-server #info channel.</summary>
    public const string ServerInfo = "info";

    /// <summary>Key for the per-server #teamchat channel.</summary>
    public const string ServerTeamChat = "teamchat";

    /// <summary>Key for the per-server #clanchat channel (provisioned only while a clan exists).</summary>
    public const string ServerClanChat = "clanchat";

    /// <summary>Key for the per-server #claninfo channel (provisioned only while a clan exists).</summary>
    public const string ServerClanInfo = "claninfo";

    /// <summary>Key for the per-server #events channel.</summary>
    public const string ServerEvents = "events";

    /// <summary>Key for the per-server #player-events channel (team presence: join, leave, death, AFK).</summary>
    public const string ServerPlayerEvents = "playerevents";

    /// <summary>The per-server rendered-map channel.</summary>
    public const string ServerMap = "map";

    /// <summary>Key for the per-server #switches channel.</summary>
    public const string ServerSwitches = "switches";

    /// <summary>Key for the per-server #alarms channel.</summary>
    public const string ServerAlarms = "alarms";

    /// <summary>Per-server storage-monitors channel key.</summary>
    public const string ServerStorageMonitors = "storagemonitors";

    /// <summary>Key for the per-server #vending channel (undercut and sell-out notifications).</summary>
    public const string ServerVending = "vending";
}

/// <summary>Stable message keys persisted as <c>ProvisionedMessage.MessageKey</c>.</summary>
internal static class WorkspaceMessageKeys
{
    /// <summary>Key for the main information message.</summary>
    public const string InformationMain = "information.main";

    /// <summary>Key for the main setup message.</summary>
    public const string SetupMain = "setup.main";

    /// <summary>Key for the main settings message.</summary>
    public const string SettingsMain = "settings.main";

    /// <summary>Key for the per-server #info map message (reconciled RustMaps monument-icon render).</summary>
    public const string ServerInfoMap = "server.info.map";

    /// <summary>Key for the per-server info message.</summary>
    public const string ServerInfo = "server.info";

    /// <summary>Key for the per-server #info events embed (cargo/heli/chinook/rigs). Rendered by Features.Events.</summary>
    public const string ServerEvents = "server.events";

    /// <summary>Key for the per-server #info team embed (roster + presence). Rendered by Features.Players.</summary>
    public const string ServerTeam = "server.team";

    /// <summary>Key for the per-server map control message (layer toggles).</summary>
    public const string ServerMap = "server.map";

    /// <summary>Key for the pinned clan overview embed. Rendered by Features.Clans.</summary>
    public const string ClanOverview = "clan.overview";

    /// <summary>Key for the pinned clan roster embed. Rendered by Features.Clans.</summary>
    public const string ClanRoster = "clan.roster";

    /// <summary>Key for the pinned clan invites embed. Rendered by Features.Clans.</summary>
    public const string ClanInvites = "clan.invites";
}

/// <summary>Stable capability names gating optional workspace channels.</summary>
internal static class WorkspaceCapabilities
{
    /// <summary>Gates the per-server clan channels; available only while the paired player is in a clan.</summary>
    public const string Clan = "clan";
}
