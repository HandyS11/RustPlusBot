namespace RustPlusBot.Features.Connections.Servers;

/// <summary>The outcome of resolving a slash command's target server.</summary>
/// <param name="ServerId">The resolved server id, or null on error.</param>
/// <param name="Name">The resolved server name, or null on error.</param>
/// <param name="ErrorMessage">A localized error to show instead, or null on success.</param>
public sealed record ServerResolution(Guid? ServerId, string? Name, string? ErrorMessage);
