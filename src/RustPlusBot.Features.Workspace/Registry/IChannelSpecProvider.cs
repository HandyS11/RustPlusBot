namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>A subsystem's contribution of channel specs. Implementations are aggregated via DI.</summary>
internal interface IChannelSpecProvider
{
    /// <summary>The channel specs this subsystem contributes.</summary>
    IEnumerable<ChannelSpec> GetChannelSpecs();
}
