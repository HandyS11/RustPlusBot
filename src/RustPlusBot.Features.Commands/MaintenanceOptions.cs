namespace RustPlusBot.Features.Commands;

/// <summary>
/// Gates the dangerous maintenance command (/admin reset-database). Bound to the same "Workspace"
/// config section as WorkspaceOptions, so a single EnableDangerCommands flag governs all danger commands.
/// </summary>
public sealed class MaintenanceOptions
{
    /// <summary>Enables the dangerous /admin reset-database command.</summary>
    public bool EnableDangerCommands { get; set; }
}
