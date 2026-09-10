namespace RustPlusBot.Persistence.Tests;

/// <summary>A <see cref="TimeProvider"/> pinned to an instant the test moves by hand.</summary>
/// <param name="now">The instant to report until <see cref="Now"/> is set.</param>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>The instant every stamp uses. Assign to advance the clock.</summary>
    public DateTimeOffset Now { get; set; } = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;
}
