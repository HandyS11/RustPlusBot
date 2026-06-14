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

    /// <summary>Key for the per-server info message.</summary>
    public const string ServerInfo = "server.info";
}
