namespace RustPlusBot.Abstractions.Connections;

/// <summary>
/// Which map-grid convention to use. The in-game (F1) map and the Rust+ companion app / RustMaps
/// website disagree on where grid ROWS sit: the in-game map anchors row 0 at the world's north edge,
/// while Rust+/RustMaps draw the rows 100 game-units further south. Columns are identical in both.
/// </summary>
public enum MapGridStyle
{
    /// <summary>Match the in-game (F1) map — grid references read exactly like in-game callouts.</summary>
    InGame = 0,

    /// <summary>Match the Rust+ companion app and rustmaps.com — rows sit 100 game-units south of in-game.</summary>
    RustPlus = 1,
}
