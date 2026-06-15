namespace RustPlusBot.Features.Pairing;

/// <summary>Pairing feature configuration, bound from the "Pairing" config section.</summary>
public sealed class PairingOptions
{
    /// <summary>How long the modal connect waits for an initial FCM check-in before reporting "still verifying".</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>First delay before retrying a timed-out initial connect.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Cap on the exponential backoff between connect retries.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
