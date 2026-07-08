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
        var fixture = Directory.Exists(FixtureDir)
            ? Directory.EnumerateFiles(FixtureDir, "rustmaps-*.json").FirstOrDefault()
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

            var x = coords.GetProperty("x").GetSingle();
            var y = coords.GetProperty("y").GetSingle();
            var (px, py) = projection.ToPixel(x, y);

            // Independent re-derivation: the cell computed from the projected PIXEL must equal the
            // cell computed from the WORLD coordinate. Catches any scale/offset/Y-flip regression.
            var cells = MapGrid.CellCount(size);
            var cellPx = MapGrid.CellSize * (2048f / size);
            var colFromPixel = Math.Clamp((int)(px / cellPx), 0, cells - 1);
            var rowFromPixel = Math.Clamp((int)(py / cellPx), 0, cells - 1);
            var colFromWorld = Math.Clamp((int)(Math.Clamp(x, 0f, size - 1) / MapGrid.CellSize), 0, cells - 1);
            var rowFromWorld =
                cells - 1 - Math.Clamp((int)(Math.Clamp(y, 0f, size - 1) / MapGrid.CellSize), 0, cells - 1);
            Assert.Equal(colFromWorld, colFromPixel);
            Assert.Equal(rowFromWorld, rowFromPixel);
        }
    }
}
