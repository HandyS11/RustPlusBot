using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!vtrack &lt;grid&gt; — registers a grid cell so every machine in it counts as the team's own.</summary>
/// <param name="trackService">Registers and reads grid/listing tracking.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class VTrackCommandHandler(IVendingTrackService trackService, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "vtrack";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Args.Count == 0)
        {
            return localizer.Get("command.vtrack.usage", context.Culture);
        }

        var grid = context.Args[0];
        var result = await trackService
            .TrackGridAsync(context.GuildId, context.ServerId, grid, context.SenderSteamId, cancellationToken)
            .ConfigureAwait(false);

        return result.GridValid
            ? localizer.Get("command.vtrack.ok", context.Culture, grid, result.MachinesFound, result.ListingsTracked)
            : localizer.Get("command.vtrack.badgrid", context.Culture, grid);
    }
}
