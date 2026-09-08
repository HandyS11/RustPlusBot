using RustPlusBot.Abstractions.Devices;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Resolves the per-server #storagemonitors channel (used to post/edit storage-monitor embeds).
/// Adds nothing to <see cref="IDeviceChannelLocator"/>; it exists so DI can bind the
/// #storagemonitors locator distinctly.
/// </summary>
public interface IStorageMonitorChannelLocator : IDeviceChannelLocator;
