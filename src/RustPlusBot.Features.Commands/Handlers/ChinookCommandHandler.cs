using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!chinook — reports the Chinook helicopter's current grid position, if any.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">For "how long ago".</param>
/// <param name="mapSettings">Supplies the server's grid style.</param>
internal sealed class ChinookCommandHandler(
    IEventState state,
    ILocalizer localizer,
    IClock clock,
    IMapSettingsStore mapSettings)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "chinook";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await MarkerReply
            .ForAsync(new MarkerReplyServices(state, localizer, clock, mapSettings), context, MarkerKind.Chinook,
                "command.chinook", cancellationToken)
            .ConfigureAwait(false);
    }
}
