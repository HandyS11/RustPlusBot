using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Specs;

/// <summary>Contributes the global category's channels and messages.</summary>
internal sealed class GlobalWorkspaceSpecProvider : IChannelSpecProvider, IMessageSpecProvider
{
    /// <inheritdoc />
    public IEnumerable<ChannelSpec> GetChannelSpecs() =>
    [
        new(WorkspaceScope.Global, WorkspaceChannelKeys.Information, "channel.information.name",
            ChannelPermissionProfile.ReadOnly, 0),
        new(WorkspaceScope.Global, WorkspaceChannelKeys.Setup, "channel.setup.name", ChannelPermissionProfile.ReadOnly,
            1),
        new(WorkspaceScope.Global, WorkspaceChannelKeys.Settings, "channel.settings.name",
            ChannelPermissionProfile.ReadOnly, 2),
    ];

    /// <inheritdoc />
    public IEnumerable<MessageSpec> GetMessageSpecs() =>
    [
        new(WorkspaceScope.Global, WorkspaceMessageKeys.InformationMain, WorkspaceChannelKeys.Information),
        new(WorkspaceScope.Global, WorkspaceMessageKeys.SetupMain, WorkspaceChannelKeys.Setup),
        new(WorkspaceScope.Global, WorkspaceMessageKeys.SettingsMain, WorkspaceChannelKeys.Settings),
    ];
}
