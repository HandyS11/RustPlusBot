using Discord;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Rendering;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchEmbedRendererTests
{
    private static SwitchEmbedRenderer Create() =>
        new(new SwitchLocalizer(SwitchLocalizationCatalog.Default));

    private static SmartSwitch Sample(string name = "Front gate", bool lastActive = false) => new()
    {
        GuildId = 10UL,
        ServerId = Guid.NewGuid(),
        EntityId = 42UL,
        Name = name,
        LastIsActive = lastActive,
    };

    [Fact]
    public void RenderSwitch_on_shows_on_status_and_off_button_enabled()
    {
        var (embed, components) = Create().RenderSwitch(Sample(), isActive: true, "en");

        Assert.Contains("ON", embed.Description ?? embed.Title ?? string.Empty, StringComparison.Ordinal);
        // Find the on/off buttons by custom id and assert disabled-state reflects current value.
        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<ButtonComponent>().ToList();
        var onBtn = buttons.Single(b => b.CustomId!.StartsWith(SwitchComponentIds.OnPrefix, StringComparison.Ordinal));
        var offBtn =
            buttons.Single(b => b.CustomId!.StartsWith(SwitchComponentIds.OffPrefix, StringComparison.Ordinal));
        Assert.True(onBtn.IsDisabled); // already on
        Assert.False(offBtn.IsDisabled);
    }

    [Fact]
    public void RenderSwitch_off_shows_off_status_and_on_button_enabled()
    {
        var (embed, components) = Create().RenderSwitch(Sample(), isActive: false, "en");

        Assert.Contains("OFF", embed.Description ?? embed.Title ?? string.Empty, StringComparison.Ordinal);
        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<ButtonComponent>().ToList();
        var onBtn = buttons.Single(b => b.CustomId!.StartsWith(SwitchComponentIds.OnPrefix, StringComparison.Ordinal));
        var offBtn =
            buttons.Single(b => b.CustomId!.StartsWith(SwitchComponentIds.OffPrefix, StringComparison.Ordinal));
        Assert.False(onBtn.IsDisabled);
        Assert.True(offBtn.IsDisabled); // already off
    }

    [Fact]
    public void RenderSwitch_unreachable_disables_all_control_buttons()
    {
        var (embed, components) = Create().RenderSwitch(Sample(), isActive: null, "en");

        Assert.Contains("Unreachable", embed.Description ?? string.Empty, StringComparison.Ordinal);
        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<ButtonComponent>().ToList();
        Assert.All(buttons, b => Assert.True(b.IsDisabled));
    }

    [Fact]
    public void RenderSwitch_french_uses_french_status()
    {
        var (embed, _) = Create().RenderSwitch(Sample(), isActive: true, "fr");
        Assert.Contains("ALLUMÉ", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderPrompt_carries_accept_and_dismiss_with_identity_tail()
    {
        var serverId = Guid.NewGuid();
        var (_, components) = Create().RenderPrompt(serverId, 42UL, "Switch 42", "en");

        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<ButtonComponent>().ToList();
        Assert.Contains(buttons, b =>
            b.CustomId == $"{SwitchComponentIds.AcceptPrefix}{serverId}:42");
        Assert.Contains(buttons, b =>
            b.CustomId == $"{SwitchComponentIds.DismissPrefix}{serverId}:42");
    }
}
