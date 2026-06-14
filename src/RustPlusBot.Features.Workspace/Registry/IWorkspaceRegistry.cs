namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregated, ordered view of all contributed channel/message specs.</summary>
internal interface IWorkspaceRegistry
{
    /// <summary>Channel specs for a scope, ordered by <see cref="ChannelSpec.Order"/>.</summary>
    /// <param name="scope">The scope to filter by.</param>
    /// <returns>The ordered channel specs.</returns>
    IReadOnlyList<ChannelSpec> GetChannelSpecs(WorkspaceScope scope);

    /// <summary>Message specs for a scope.</summary>
    /// <param name="scope">The scope to filter by.</param>
    /// <returns>The message specs.</returns>
    IReadOnlyList<MessageSpec> GetMessageSpecs(WorkspaceScope scope);
}
