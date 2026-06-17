namespace RustPlusBot.Abstractions.Events;

/// <summary>Which oil rig a rig event refers to.</summary>
public enum RigKind
{
    /// <summary>The small oil rig (monument token <c>oilrig_1</c>).</summary>
    Small = 0,

    /// <summary>The large oil rig (monument token <c>large_oil_rig</c>).</summary>
    Large = 1,
}
