using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!vuntrack &lt;grid&gt; — unregisters a grid cell.</summary>
/// <param name="trackService">Registers and reads grid/listing tracking.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class VUntrackCommandHandler(IVendingTrackService trackService, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "vuntrack";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Args.Count == 0)
        {
            return localizer.Get("command.vuntrack.usage", context.Culture);
        }

        var grid = context.Args[0];
        var removed = await trackService
            .UntrackGridAsync(context.GuildId, context.ServerId, grid, cancellationToken)
            .ConfigureAwait(false);

        return removed
            ? localizer.Get("command.vuntrack.ok", context.Culture, grid)
            : localizer.Get("command.vuntrack.notracked", context.Culture, grid);
    }
}
