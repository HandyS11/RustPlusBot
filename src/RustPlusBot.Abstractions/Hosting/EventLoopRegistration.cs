using Microsoft.Extensions.Logging;

namespace RustPlusBot.Abstractions.Hosting;

/// <summary>One named event-consumption loop owned by an <see cref="EventLoopHostedService"/>.</summary>
/// <remarks>
/// A registration is created by <see cref="EventLoopHostedService.Loop{TEvent}"/>, which subscribes to the
/// bus <em>as the registration is built</em>. Holding the already-established stream in the closure is what
/// lets the owning service finish <c>StartAsync</c> with every subscription live.
/// </remarks>
public sealed class EventLoopRegistration
{
    internal EventLoopRegistration(string name, Func<ILogger, CancellationToken, Task> runAsync)
    {
        Name = name;
        RunAsync = runAsync;
    }

    /// <summary>Gets the loop's name, used in the "loop faulted" log message.</summary>
    public string Name { get; }

    /// <summary>Gets the delegate that drains the loop's already-established subscription.</summary>
    internal Func<ILogger, CancellationToken, Task> RunAsync { get; }
}
