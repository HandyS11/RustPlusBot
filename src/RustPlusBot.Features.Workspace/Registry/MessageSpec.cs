namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Declarative description of an anchored message the bot keeps in a channel.</summary>
/// <param name="Scope">Global or per-server.</param>
/// <param name="Key">Stable key (persisted as MessageKey); also the renderer lookup key.</param>
/// <param name="ChannelKey">The <see cref="ChannelSpec.Key"/> of the channel it lives in.</param>
/// <param name="Pinned">True to pin the message when it is first posted, so a channel that also
/// carries transient messages keeps this one reachable from the pin bar.</param>
internal sealed record MessageSpec(WorkspaceScope Scope, string Key, string ChannelKey, bool Pinned = false);
