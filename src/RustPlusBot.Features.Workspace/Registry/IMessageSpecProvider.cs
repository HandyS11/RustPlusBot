namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>A subsystem's contribution of message specs. Implementations are aggregated via DI.</summary>
internal interface IMessageSpecProvider
{
    /// <summary>The message specs this subsystem contributes.</summary>
    IEnumerable<MessageSpec> GetMessageSpecs();
}
