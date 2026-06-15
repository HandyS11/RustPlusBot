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
}
