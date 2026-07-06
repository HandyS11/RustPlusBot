using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Discord.Tests.Posting;

public sealed class RenderGateTests
{
    [Fact]
    public void First_render_for_a_message_should_send()
    {
        var gate = new RenderGate();

        Assert.True(gate.ShouldSend(900UL, "render-a"));
    }

    [Fact]
    public void Committed_identical_render_should_not_send()
    {
        var gate = new RenderGate();
        gate.Commit(900UL, "render-a");

        Assert.False(gate.ShouldSend(900UL, "render-a"));
    }

    [Fact]
    public void Committed_then_different_render_should_send()
    {
        var gate = new RenderGate();
        gate.Commit(900UL, "render-a");

        Assert.True(gate.ShouldSend(900UL, "render-b"));
    }

    [Fact]
    public void Invalidate_forces_the_next_identical_render_to_send()
    {
        var gate = new RenderGate();
        gate.Commit(900UL, "render-a");
        gate.Invalidate(900UL);

        Assert.True(gate.ShouldSend(900UL, "render-a"));
    }

    [Fact]
    public void Invalidate_on_an_untracked_message_is_a_no_op()
    {
        var gate = new RenderGate();

        gate.Invalidate(901UL);

        Assert.True(gate.ShouldSend(901UL, "render-a"));
    }

    [Fact]
    public void Messages_are_tracked_independently()
    {
        var gate = new RenderGate();
        gate.Commit(900UL, "render-a");

        Assert.True(gate.ShouldSend(901UL, "render-a"));
        Assert.False(gate.ShouldSend(900UL, "render-a"));
    }
}
