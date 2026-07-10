# RustPlusBot.MapParity

Dev-only manual tool that proves our `MapProjection` math agrees with RustMaps' own
ground-truth monument placement. It fetches a map from the RustMaps v4 API (rendered
`ImageUrl`, which has RustMaps' own monument icons baked in, plus the monument list),
projects every monument's world coordinate through our `MapProjection`, and draws a
magenta crosshair at the resulting pixel on a copy of RustMaps' render. If our
transform is correct, every crosshair lands squarely on the matching RustMaps monument
icon — an eyeball diff, not an assertion.

## Usage

```bash
dotnet run --project tools/RustPlusBot.MapParity -- <worldSize> <seed> <apiKey> <outDir>
```

This writes `<outDir>/rustmaps-render.png` (the untouched RustMaps render),
`<outDir>/overlay.png` (the render with crosshairs — inspect this), and
`<outDir>/monuments.json` (the serialized RustMaps `MapInfo` response, including the
monument list consumed by the parity test).

Once the crosshairs check out, commit `monuments.json` to
`tests/RustPlusBot.Features.Map.Tests/Fixtures/rustmaps-<size>-<seed>.json` so
`RustMapsParityTests` picks it up (it skips automatically when no fixture exists).
