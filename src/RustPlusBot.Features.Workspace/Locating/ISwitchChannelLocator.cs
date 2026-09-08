using RustPlusBot.Abstractions.Devices;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Resolves the per-server #switches channel (used to post/edit switch embeds). Adds nothing to
/// <see cref="IDeviceChannelLocator"/>; it exists so DI can bind the #switches locator distinctly.
/// </summary>
public interface ISwitchChannelLocator : IDeviceChannelLocator;
