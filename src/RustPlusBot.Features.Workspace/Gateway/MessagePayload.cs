using Discord;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>A renderable message: optional text, embed, and components.</summary>
/// <param name="Text">Plain content, or null.</param>
/// <param name="Embed">An embed, or null.</param>
/// <param name="Components">Message components (buttons/selects), or null.</param>
public sealed record MessagePayload(string? Text, Embed? Embed, MessageComponent? Components);
