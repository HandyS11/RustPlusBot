namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>Everything a command handler needs for one invocation.</summary>
/// <param name="GuildId">Owning guild.</param>
/// <param name="ServerId">Target server.</param>
/// <param name="Culture">Guild culture (e.g. "en"/"fr") for the reply.</param>
/// <param name="SenderSteamId">Steam id of the in-game caller.</param>
/// <param name="SenderName">In-game name of the caller.</param>
/// <param name="Args">Parsed command arguments.</param>
internal sealed record CommandContext(
    ulong GuildId,
    Guid ServerId,
    string Culture,
    ulong SenderSteamId,
    string SenderName,
    IReadOnlyList<string> Args);
