namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Which desired-state scope a spec belongs to.</summary>
internal enum WorkspaceScope
{
    /// <summary>The single global RustPlusBot category per guild.</summary>
    Global = 0,

    /// <summary>A per-registered-server category.</summary>
    PerServer = 1,
}
