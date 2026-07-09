namespace RustPlusBot.Features.Workspace;

/// <summary>
/// Public custom ids for components rendered into the workspace by Workspace but handled by other features.
/// Feature modules reference these so the rendered control and its handler share one contract.
/// </summary>
public static class WorkspaceComponentIds
{
    /// <summary>The #setup "Connect account" button; handled by the Pairing feature.</summary>
    public const string ConnectAccount = "workspace:setup:connect";

    /// <summary>Prefix for the #info "swap active player" select; the server id is appended. Handled by Connections.</summary>
    public const string ServerInfoSwapPrefix = "workspace:info:swap:";

    /// <summary>The #setup "Disconnect account" button; handled by the Pairing feature.</summary>
    public const string DisconnectAccount = "workspace:setup:disconnect";

    /// <summary>Prefix for the #info "remove server" button; the server id is appended. Handled by Connections.</summary>
    public const string ServerInfoRemovePrefix = "workspace:info:remove:";

    /// <summary>Prefix for a #map layer toggle button; "{layer}:{serverId}" is appended. Handled by Workspace.</summary>
    public const string MapTogglePrefix = "workspace:map:toggle:";

    /// <summary>Prefix for a #map grid-style button; "{style}:{serverId}" is appended. Handled by Workspace.</summary>
    public const string MapGridStylePrefix = "workspace:map:gridstyle:";
}
