namespace RustPlusBot.Features.Events.Classifying;

/// <summary>A classified live map event.</summary>
public enum MapEventKind
{
    /// <summary>A cargo ship entered the map.</summary>
    CargoEntered = 0,

    /// <summary>A cargo ship left the map.</summary>
    CargoLeft = 1,

    /// <summary>A patrol helicopter entered the map.</summary>
    HeliEntered = 2,

    /// <summary>A patrol helicopter left the map.</summary>
    HeliLeft = 3,

    /// <summary>A Chinook spawned.</summary>
    ChinookSpawned = 4,
}
