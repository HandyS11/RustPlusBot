namespace RustPlusBot.Abstractions.Time;

/// <summary>An <see cref="IClock"/> backed by the wall clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
