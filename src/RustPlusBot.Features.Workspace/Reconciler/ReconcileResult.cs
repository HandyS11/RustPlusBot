namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Outcome of a reconcile.</summary>
internal enum ReconcileStatus
{
    /// <summary>The scope was converged.</summary>
    Provisioned = 0,

    /// <summary>The bot lacks required guild permissions; nothing was changed.</summary>
    MissingPermissions = 1,

    /// <summary>Nothing was done because the target no longer exists (e.g. an unknown server).</summary>
    Skipped = 2,
}

/// <summary>Result of a reconcile, including any missing permissions.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="MissingPermissions">Missing permission names when <see cref="ReconcileStatus.MissingPermissions"/>.</param>
internal sealed record ReconcileResult(ReconcileStatus Status, IReadOnlyList<string> MissingPermissions)
{
    /// <summary>A successful provision result.</summary>
    public static ReconcileResult Provisioned { get; } = new(ReconcileStatus.Provisioned, []);

    /// <summary>A skipped (no-op) result, e.g. the target no longer exists.</summary>
    public static ReconcileResult Skipped { get; } = new(ReconcileStatus.Skipped, []);

    /// <summary>Builds a missing-permissions result.</summary>
    /// <param name="permissions">The missing permission names.</param>
    /// <returns>A missing-permissions result.</returns>
    public static ReconcileResult Missing(IReadOnlyList<string> permissions) => new(ReconcileStatus.MissingPermissions, permissions);
}
