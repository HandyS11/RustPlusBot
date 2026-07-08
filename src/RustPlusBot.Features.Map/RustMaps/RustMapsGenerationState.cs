namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Lifecycle of a RustMaps map for one (size, seed). Ready/Failed/LimitReached are terminal for the wipe.</summary>
public enum RustMapsGenerationState
{
    /// <summary>Not yet checked/started.</summary>
    Idle = 0,

    /// <summary>Generation requested; polling for completion.</summary>
    Generating = 1,

    /// <summary>The RustMaps render is available and cached.</summary>
    Ready = 2,

    /// <summary>Generation failed; no retry this wipe.</summary>
    Failed = 3,

    /// <summary>Generation skipped because the RustMaps credit limit is reached.</summary>
    LimitReached = 4,
}
