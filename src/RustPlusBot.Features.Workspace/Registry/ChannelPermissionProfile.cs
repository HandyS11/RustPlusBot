namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>The permission shape applied to a provisioned channel.</summary>
internal enum ChannelPermissionProfile
{
    /// <summary>Members can view but not send; the bot manages content.</summary>
    ReadOnly = 0,

    /// <summary>Members can send (reserved for later chat/command channels).</summary>
    Interactive = 1,
}
