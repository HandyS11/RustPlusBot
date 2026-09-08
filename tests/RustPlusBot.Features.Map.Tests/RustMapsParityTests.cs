using System.Text.Json;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Map.Rendering;

namespace RustPlusBot.Features.Map.Tests;

/// <summary>
/// Ground-truth parity: monuments from a committed RustMaps response, projected through our
/// MapProjection, must land in the grid cells RustMaps world coordinates imply. Skips when no
/// fixture has been generated yet (tools/RustPlusBot.MapParity produces one).
/// </summary>
public sealed class RustMapsParityTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [SkippableFact]
    public void Monument_world_coords_project_into_consistent_grid_cells()
    {
        // Order the enumeration so the selected fixture is deterministic across machines/filesystems.
        var fixture = Directory.Exists(FixtureDir)
            ? Directory.EnumerateFiles(FixtureDir, "rustmaps-*.json").Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
        Skip.If(fixture is null, "No RustMaps fixture committed yet (run tools/RustPlusBot.MapParity).");

        using var doc = JsonDocument.Parse(File.ReadAllText(fixture));
        var root = doc.RootElement;
        var size = (uint)root.GetProperty("size").GetInt32();
        var projection = new MapProjection(size, 2048, 2048, 0, 2048);

        foreach (var monument in root.GetProperty("monuments").EnumerateArray())
        {
            if (!monument.TryGetProperty("coordinates", out var coords) || coords.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            // RustMaps reports the game's own world coordinates, whose origin is the map CENTRE; Rust+ (and
            // therefore MapProjection) works from the map corner, so shift the origin before projecting.
            var x = coords.GetProperty("x").GetSingle() + (size / 2f);
            var y = coords.GetProperty("y").GetSingle() + (size / 2f);
            var (px, py) = projection.ToPixel(x, y);

            // Independent re-derivation: the cell computed from the projected PIXEL must equal the cell
            // MapGrid labels the WORLD coordinate with. Catches any scale/offset/Y-flip regression, and
            // pins the projection against the grid the rest of the bot quotes to players.
            var cells = MapGrid.CellCount(size);
            var cellPx = MapGrid.CellSize * (2048f / size);
            var col = Math.Clamp((int)(px / cellPx), 0, cells - 1);
            var row = Math.Clamp((int)(py / cellPx), 0, cells - 1);
            Assert.Equal(MapGrid.LabelFor(x, y, size), $"{MapGrid.ColumnLetters(col)}{row}");
        }
    }
}
