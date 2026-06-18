using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One teammate to draw, already projected to pixel coordinates.</summary>
/// <param name="Name">The player display name.</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
/// <param name="IsAlive">Whether the player is alive (dead players are styled differently).</param>
/// <param name="IsOnline">Whether the player is online.</param>
public sealed record PlayerPlacement(string Name, float PixelX, float PixelY, bool IsAlive, bool IsOnline);

/// <summary>One monument to draw, already projected to pixel coordinates.</summary>
/// <param name="Token">The monument protobuf token (selects the icon).</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
public sealed record MonumentPlacement(string Token, float PixelX, float PixelY);

/// <summary>One oil rig to draw with activation styling, already projected to pixel coordinates.</summary>
/// <param name="Kind">Which rig.</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
/// <param name="Active">Whether the rig is currently active (combat window).</param>
public sealed record RigPlacement(RigKind Kind, float PixelX, float PixelY, bool Active);
