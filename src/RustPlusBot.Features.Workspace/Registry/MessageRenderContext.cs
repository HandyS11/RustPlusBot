namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Context handed to a renderer to build a message payload.</summary>
/// <param name="GuildId">The guild being rendered for.</param>
/// <param name="ServerId">The server scope, or null for global messages.</param>
/// <param name="Culture">The guild's BCP-47 culture.</param>
public sealed record MessageRenderContext(ulong GuildId, Guid? ServerId, string Culture);
