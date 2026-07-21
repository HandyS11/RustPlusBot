namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Declarative description of a channel the bot should keep provisioned.</summary>
/// <param name="Scope">Global or per-server.</param>
/// <param name="Key">Stable key (persisted as ChannelKey).</param>
/// <param name="NameKey">i18n key resolving to the channel name.</param>
/// <param name="Permissions">Permission profile to apply.</param>
/// <param name="Order">Sort order within the category.</param>
/// <param name="Capability">Optional capability gating this channel; null means always provisioned.</param>
internal sealed record ChannelSpec(
    WorkspaceScope Scope,
    string Key,
    string NameKey,
    ChannelPermissionProfile Permissions,
    int Order,
    string? Capability = null);
