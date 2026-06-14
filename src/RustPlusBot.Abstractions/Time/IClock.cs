namespace RustPlusBot.Abstractions.Time;

/// <summary>Abstracts the system clock so time-dependent logic is testable.</summary>
public interface IClock
{
    /// <summary>The current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
