namespace RustPlusBot.Features.Workspace;

/// <summary>Workspace feature configuration, bound from the "Workspace" config section.</summary>
public sealed class WorkspaceOptions
{
    /// <summary>Enables dangerous developer commands (/workspace reset, /workspace simulate-server).</summary>
    public bool EnableDangerCommands { get; set; }

    /// <summary>
    ///     How often every connected server's #info embeds are re-rendered. Unchanged renders are
    ///     suppressed by the render gate, so the steady-state cost is up to four Rust+ calls per
    ///     connected server per tick (server info, time, team info and map dimensions) and at most
    ///     three Discord edits. A disconnected server costs no round-trips — its live queries return
    ///     null immediately.
    /// </summary>
    public TimeSpan InfoRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);
}
