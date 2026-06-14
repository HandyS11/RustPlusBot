namespace RustPlusBot.Features.Workspace;

/// <summary>Workspace feature configuration, bound from the "Workspace" config section.</summary>
public sealed class WorkspaceOptions
{
    /// <summary>Enables dangerous developer commands (/workspace reset, /workspace simulate-server).</summary>
    public bool EnableDangerCommands { get; set; }
}
