using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!unmute — re-enable bot-to-game output.</summary>
/// <param name="store">The mute store.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class UnmuteCommandHandler(IMuteStore store, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "unmute";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await store.SetMutedAsync(context.GuildId, context.ServerId, false, cancellationToken).ConfigureAwait(false);
        return localizer.Get("command.unmute.done", context.Culture);
    }
}
