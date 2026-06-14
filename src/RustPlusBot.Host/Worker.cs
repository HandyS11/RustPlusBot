using System.Diagnostics.CodeAnalysis;

namespace RustPlusBot.Host;

/// <summary>Placeholder background service; will be replaced in a later task.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by the DI container.")]
internal sealed class Worker : BackgroundService
{
    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.CompletedTask;
}
