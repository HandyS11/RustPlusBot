namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A raw clan chat line as received from a socket (before active-player classification).</summary>
/// <param name="SteamId">The Steam64 id of the sender.</param>
/// <param name="Name">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
/// <param name="Time">When the line was sent (UTC).</param>
internal sealed record ClanChatLine(ulong SteamId, string Name, string Message, DateTimeOffset Time);
