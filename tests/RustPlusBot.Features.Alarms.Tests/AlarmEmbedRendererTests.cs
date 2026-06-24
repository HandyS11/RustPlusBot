using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Alarms.Tests;

public sealed class AlarmEmbedRendererTests
{
    private static readonly DateTimeOffset _fixedNow = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static AlarmEmbedRenderer Create(DateTimeOffset? now = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now ?? _fixedNow);
        var localizer = new ResxLocalizer();
        return new AlarmEmbedRenderer(localizer, clock);
    }

    private static SmartAlarm Sample(
        string name = "Fire Alarm",
        bool pingEveryone = false,
        bool relayToTeamChat = false,
        bool lastIsActive = false,
        DateTimeOffset? lastTriggeredUtc = null) => new()
    {
        GuildId = 10UL,
        ServerId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        EntityId = 42UL,
        Name = name,
        PingEveryone = pingEveryone,
        RelayToTeamChat = relayToTeamChat,
        LastIsActive = lastIsActive,
        LastTriggeredUtc = lastTriggeredUtc,
    };

    private static List<ButtonComponent> Buttons(MessageComponent components) =>
    [
        .. components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components)
            .OfType<ButtonComponent>()
    ];

    // ── Status ────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_lastIsActive_false_shows_armed_status()
    {
        var (embed, _) = Create().RenderAlarm(Sample(lastIsActive: false), unreachable: false, "en");

        Assert.Contains("Armed", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_lastIsActive_true_shows_active_status()
    {
        var (embed, _) = Create().RenderAlarm(Sample(lastIsActive: true), unreachable: false, "en");

        Assert.Contains("Active", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_unreachable_shows_unreachable_status()
    {
        var (embed, _) = Create().RenderAlarm(Sample(), unreachable: true, "en");

        Assert.Contains("Unreachable", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    // ── Description / triggered text ──────────────────────────────────────────

    [Fact]
    public void RenderAlarm_never_triggered_shows_never_triggered_text()
    {
        var (embed, _) = Create().RenderAlarm(Sample(lastTriggeredUtc: null), unreachable: false, "en");

        Assert.Contains("Never triggered", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_triggered_recently_shows_last_triggered_ago()
    {
        var triggered = _fixedNow.AddMinutes(-5);
        var (embed, _) = Create().RenderAlarm(Sample(lastTriggeredUtc: triggered), unreachable: false, "en");

        Assert.Contains("Last triggered", embed.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("ago", embed.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("5m", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_triggered_hours_ago_shows_hours_format()
    {
        var triggered = _fixedNow.AddHours(-2).AddMinutes(-10);
        var (embed, _) = Create().RenderAlarm(Sample(lastTriggeredUtc: triggered), unreachable: false, "en");

        Assert.Contains("2h", embed.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("10m", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_triggered_days_ago_shows_days_format()
    {
        var triggered = _fixedNow.AddDays(-3);
        var (embed, _) = Create().RenderAlarm(Sample(lastTriggeredUtc: triggered), unreachable: false, "en");

        Assert.Contains("3d", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    // ── Buttons — ping ────────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_ping_off_button_is_secondary_style()
    {
        var (_, components) = Create().RenderAlarm(Sample(pingEveryone: false), unreachable: false, "en");
        var btn = Buttons(components).Single(b =>
            b.CustomId!.StartsWith(AlarmComponentIds.PingTogglePrefix, StringComparison.Ordinal));

        Assert.Equal(ButtonStyle.Secondary, btn.Style);
        Assert.False(btn.IsDisabled);
    }

    [Fact]
    public void RenderAlarm_ping_on_button_is_success_style()
    {
        var (_, components) = Create().RenderAlarm(Sample(pingEveryone: true), unreachable: false, "en");
        var btn = Buttons(components).Single(b =>
            b.CustomId!.StartsWith(AlarmComponentIds.PingTogglePrefix, StringComparison.Ordinal));

        Assert.Equal(ButtonStyle.Success, btn.Style);
        Assert.False(btn.IsDisabled);
    }

    // ── Buttons — relay ───────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_relay_off_button_is_secondary_style()
    {
        var (_, components) = Create().RenderAlarm(Sample(relayToTeamChat: false), unreachable: false, "en");
        var btn = Buttons(components).Single(b =>
            b.CustomId!.StartsWith(AlarmComponentIds.RelayTogglePrefix, StringComparison.Ordinal));

        Assert.Equal(ButtonStyle.Secondary, btn.Style);
        Assert.False(btn.IsDisabled);
    }

    [Fact]
    public void RenderAlarm_relay_on_button_is_success_style()
    {
        var (_, components) = Create().RenderAlarm(Sample(relayToTeamChat: true), unreachable: false, "en");
        var btn = Buttons(components).Single(b =>
            b.CustomId!.StartsWith(AlarmComponentIds.RelayTogglePrefix, StringComparison.Ordinal));

        Assert.Equal(ButtonStyle.Success, btn.Style);
        Assert.False(btn.IsDisabled);
    }

    // ── Buttons — unreachable disables all ───────────────────────────────────

    [Fact]
    public void RenderAlarm_unreachable_disables_all_buttons()
    {
        var (_, components) = Create().RenderAlarm(Sample(), unreachable: true, "en");
        var buttons = Buttons(components);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, b => Assert.True(b.IsDisabled));
    }

    // ── Button labels ─────────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_ping_on_label_contains_on()
    {
        var (_, components) = Create().RenderAlarm(Sample(pingEveryone: true), unreachable: false, "en");
        var btn = Buttons(components).Single(b =>
            b.CustomId!.StartsWith(AlarmComponentIds.PingTogglePrefix, StringComparison.Ordinal));

        Assert.Contains("on", btn.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderAlarm_ping_off_label_contains_off()
    {
        var (_, components) = Create().RenderAlarm(Sample(pingEveryone: false), unreachable: false, "en");
        var btn = Buttons(components).Single(b =>
            b.CustomId!.StartsWith(AlarmComponentIds.PingTogglePrefix, StringComparison.Ordinal));

        Assert.Contains("off", btn.Label, StringComparison.OrdinalIgnoreCase);
    }

    // ── Custom-id tails ───────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_buttons_have_correct_serverid_entityid_tail()
    {
        var alarm = Sample();
        var tail = $"{alarm.ServerId}:{alarm.EntityId}";
        var (_, components) = Create().RenderAlarm(alarm, unreachable: false, "en");
        var buttons = Buttons(components);

        Assert.All(buttons, b => Assert.EndsWith(tail, b.CustomId, StringComparison.Ordinal));
    }

    // ── Culture / FR ─────────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_french_armed_uses_french_status()
    {
        var (embed, _) = Create().RenderAlarm(Sample(lastIsActive: false), unreachable: false, "fr");

        Assert.Contains("Armée", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_french_never_triggered_uses_french_text()
    {
        var (embed, _) = Create().RenderAlarm(Sample(lastTriggeredUtc: null), unreachable: false, "fr");

        Assert.Contains("Jamais", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    // ── Footer ────────────────────────────────────────────────────────────────

    [Fact]
    public void RenderAlarm_footer_contains_entity_id()
    {
        var (embed, _) = Create().RenderAlarm(Sample(), unreachable: false, "en");

        Assert.NotNull(embed.Footer);
        Assert.Contains("42", embed.Footer.Value.Text, StringComparison.Ordinal);
    }

    // ── RenderPrompt ──────────────────────────────────────────────────────────

    [Fact]
    public void RenderPrompt_carries_accept_and_dismiss_with_identity_tail()
    {
        var serverId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var (_, components) = Create().RenderPrompt(serverId, 99UL, "Alarm 99", "en");
        var buttons = Buttons(components);

        Assert.Contains(buttons, b =>
            b.CustomId == AlarmComponentIds.AcceptPrefix + $"{serverId}:99");
        Assert.Contains(buttons, b =>
            b.CustomId == AlarmComponentIds.DismissPrefix + $"{serverId}:99");
    }

    [Fact]
    public void RenderPrompt_embed_title_is_new_alarm_detected()
    {
        var (embed, _) = Create().RenderPrompt(Guid.NewGuid(), 1UL, "Alarm 1", "en");

        Assert.Contains("alarm", embed.Title ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderPrompt_embed_body_contains_default_name()
    {
        var (embed, _) = Create().RenderPrompt(Guid.NewGuid(), 1UL, "My Custom Alarm", "en");

        Assert.Contains("My Custom Alarm", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderPrompt_french_uses_french_strings()
    {
        var (embed, _) = Create().RenderPrompt(Guid.NewGuid(), 1UL, "Alarme Test", "fr");

        Assert.Contains("détectée", embed.Title ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderAlarm_null_alarm_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Create().RenderAlarm(null!, unreachable: false, "en"));
    }
}
