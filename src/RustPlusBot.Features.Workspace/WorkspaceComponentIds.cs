namespace RustPlusBot.Features.Workspace;

/// <summary>
/// Public custom ids for components rendered into the workspace by Workspace but handled by other features.
/// Feature modules reference these so the rendered control and its handler share one contract.
/// </summary>
public static class WorkspaceComponentIds
{
    /// <summary>The #setup "Connect account" button; handled by the Pairing feature.</summary>
    public const string ConnectAccount = "workspace:setup:connect";
}
