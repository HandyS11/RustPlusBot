namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A raw team chat line as received from a socket (before active-player classification).</summary>
/// <param name="SteamId">The Steam64 id of the sender.</param>
/// <param name="Name">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
internal sealed record TeamChatLine(ulong SteamId, string Name, string Message);
