namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>One in-game command. The dispatcher resolves handlers by <see cref="Name"/>.</summary>
internal interface ICommandHandler
{
    /// <summary>The lowercase command name (without prefix), e.g. "pop".</summary>
    string Name { get; }

    /// <summary>Executes and returns the localized reply, or null for no reply.</summary>
    /// <param name="context">The command context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The localized reply text, or null for no reply.</returns>
    Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
