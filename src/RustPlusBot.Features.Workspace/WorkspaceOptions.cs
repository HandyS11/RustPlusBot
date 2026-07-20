namespace RustPlusBot.Features.Workspace;

/// <summary>Workspace feature configuration, bound from the "Workspace" config section.</summary>
public sealed class WorkspaceOptions
{
    /// <summary>Enables dangerous developer commands (/workspace reset, /workspace simulate-server).</summary>
    public bool EnableDangerCommands { get; set; }

    /// <summary>
    ///     How often every connected server's #info embeds are re-rendered. Unchanged renders are
    ///     suppressed by the render gate, so the steady-state cost is three Rust+ calls per server
    ///     per tick and at most three Discord edits.
    /// </summary>
    public TimeSpan InfoRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
