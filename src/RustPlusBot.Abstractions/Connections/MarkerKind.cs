namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The subset of Rust map-marker types this bot reasons about; everything else is <see cref="Other"/>.</summary>
public enum MarkerKind
{
    /// <summary>A marker type the bot does not classify (player, vending, explosion, generic radius, vendor).</summary>
    Other = 0,

    /// <summary>A cargo ship.</summary>
    CargoShip = 1,

    /// <summary>A patrol helicopter.</summary>
    PatrolHelicopter = 2,

    /// <summary>A Chinook (CH-47).</summary>
    Chinook = 3,

    /// <summary>A locked crate.</summary>
    Crate = 4,

    /// <summary>The travelling vendor.</summary>
    TravellingVendor = 5,
}
