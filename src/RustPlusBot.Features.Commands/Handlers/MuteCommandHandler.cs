using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!mute — silence all bot-to-game output.</summary>
/// <param name="store">The mute store.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class MuteCommandHandler(IMuteStore store, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "mute";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await store.SetMutedAsync(context.GuildId, context.ServerId, true, cancellationToken).ConfigureAwait(false);
        return localizer.Get("command.mute.done", context.Culture);
    }
}
