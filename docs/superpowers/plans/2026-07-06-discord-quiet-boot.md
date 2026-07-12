# Quiet Boot (Discord Edit Dedup + Boot-Sweep Suppression) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the bot from exhausting Discord's per-channel message-PATCH rate-limit bucket at boot (and during reconnect storms) by skipping no-op embed edits centrally and suppressing the boot-time unreachable sweep.

**Architecture:** `DiscordChannelMessenger` converts from a static helper to an injected singleton holding a per-message "last successfully sent render" cache (a pure `RenderGate` + `RenderCanonicalizer`); identical re-renders never reach Discord. `ConnectionStatusChangedEvent` gains `IsConnected`/`WasConnected` bools computed from in-process supervisor state so device relays only sweep embeds to unreachable on a genuine drop from Connected. Safety nets: `RetryMode.AlwaysRetry` + 30 s request timeout, and command registration moved off the gateway Ready handler.

**Tech Stack:** .NET 10 / C#, Discord.Net 3.20, xUnit + NSubstitute, EF Core (unchanged), Serilog (unchanged).

**Spec:** `docs/superpowers/specs/2026-07-06-discord-rate-limit-quiet-boot-design.md`

## Global Constraints

- Solution file is `RustPlusBot.slnx` (there is NO `.sln`). Build: `dotnet build -maxcpucount:1 RustPlusBot.slnx`. Test: `dotnet test -maxcpucount:1 RustPlusBot.slnx`.
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` and `dotnet test`** — ConfigureGitHooks races on `.git/config` otherwise, and a broken build silently DROPS an assembly's tests. Always read per-assembly test counts, not just the total.
- Build runs with `-warnaserror`: any compiler/analyzer warning (unused using, missing XML doc, CA1305 culture, CA1307/CA1310 StringComparison) fails the build.
- Tests are plain xUnit `Assert.*` + NSubstitute — NO FluentAssertions. `using Xunit` is global (never add the using line).
- Hard CI format gate: `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR` must produce **no diff**. Run it before the final commit of each task if unsure; always at the end.
- NEVER `git add` anything under `docs/product/` or `docs/superpowers/` — they are gitignored, local-only.
- Central package management: `PackageReference` entries in csproj files have NO `Version` attribute.
- Every public/internal type and member gets XML doc comments (repo convention; enforced style).
- Work on branch `feat/quiet-boot` cut from `develop` in the main checkout (no worktrees).
- Sonar coverage exclusions (`.github/workflows/Sonar.yml`) exclude `**/DiscordChannelMessenger.cs`, `**/Discord*ChannelPoster.cs`, `**/DiscordBotService.cs`, `**/*ServiceCollectionExtensions.cs` by **filename** — do not rename these files. New files `RenderGate.cs` / `RenderCanonicalizer.cs` are NOT excluded and MUST be covered by tests (Tasks 1–2).
- All commits end with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

---

### Task 1: `RenderCanonicalizer` + new `RustPlusBot.Discord.Tests` project

**Files:**
- Create: `tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj`
- Create: `tests/RustPlusBot.Discord.Tests/Posting/RenderCanonicalizerTests.cs`
- Create: `src/RustPlusBot.Discord/Posting/RenderCanonicalizer.cs`
- Modify: `RustPlusBot.slnx` (via `dotnet sln add`)

**Interfaces:**
- Consumes: Discord.Net `Embed`, `MessageComponent` (built with `EmbedBuilder`/`ComponentBuilder`).
- Produces: `public static class RenderCanonicalizer` with `public static string Canonicalize(Embed? embed, MessageComponent? components)` — deterministic, collision-safe (length-prefixed) canonical string. Task 3 calls this.

- [ ] **Step 1: Create the branch**

```bash
git -C /home/handys11/Dev/RustPlusBot checkout develop
git -C /home/handys11/Dev/RustPlusBot pull
git -C /home/handys11/Dev/RustPlusBot checkout -b feat/quiet-boot
```

- [ ] **Step 2: Create the test project and add it to the solution**

Write `tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
  </ItemGroup>

</Project>
```

Then:

```bash
dotnet sln /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx add /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj
```

Verify the project landed in the solution's tests folder alongside the other test projects (`grep Discord.Tests RustPlusBot.slnx`); if `dotnet sln add` put it at the root instead of the `/tests/` solution folder, move the entry to match how the other `tests/*` projects are declared in the slnx XML.

- [ ] **Step 3: Write the failing tests**

Write `tests/RustPlusBot.Discord.Tests/Posting/RenderCanonicalizerTests.cs`:

```csharp
using Discord;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Discord.Tests.Posting;

public sealed class RenderCanonicalizerTests
{
    private static Embed BuildEmbed(
        string title = "Switch",
        string description = "On",
        string footer = "id 42",
        uint color = 0x00FF00)
        => new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(description)
            .WithFooter(footer)
            .WithColor(new Color(color))
            .Build();

    private static MessageComponent BuildComponents(string label = "On", bool disabled = false)
        => new ComponentBuilder()
            .WithButton(label, "sw:on:42", ButtonStyle.Success, disabled: disabled)
            .Build();

    [Fact]
    public void Identical_renders_produce_identical_canonical_strings()
    {
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents());
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents());

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_description_changes_the_canonical_string()
    {
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(description: "On"), BuildComponents());
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(description: "Off"), BuildComponents());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_color_changes_the_canonical_string()
    {
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(color: 0x00FF00), BuildComponents());
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(color: 0xFF0000), BuildComponents());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_footer_changes_the_canonical_string()
    {
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(footer: "id 42"), BuildComponents());
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(footer: "id 43"), BuildComponents());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_button_label_changes_the_canonical_string()
    {
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents(label: "On"));
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents(label: "Off"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Disabling_a_button_changes_the_canonical_string()
    {
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents(disabled: false));
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents(disabled: true));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Adjacent_values_do_not_collide()
    {
        // Same concatenation, different boundaries: length-prefixing must keep these apart.
        var a = RenderCanonicalizer.Canonicalize(BuildEmbed(title: "ab", description: "c"), components: null);
        var b = RenderCanonicalizer.Canonicalize(BuildEmbed(title: "a", description: "bc"), components: null);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Fields_are_included()
    {
        var bare = new EmbedBuilder().WithTitle("T").Build();
        var withField = new EmbedBuilder().WithTitle("T").AddField("Slots", "3/24", inline: true).Build();

        Assert.NotEqual(
            RenderCanonicalizer.Canonicalize(bare, components: null),
            RenderCanonicalizer.Canonicalize(withField, components: null));
    }

    [Fact]
    public void Null_components_differ_from_button_components()
    {
        Assert.NotEqual(
            RenderCanonicalizer.Canonicalize(BuildEmbed(), components: null),
            RenderCanonicalizer.Canonicalize(BuildEmbed(), BuildComponents()));
    }

    [Fact]
    public void Select_menu_options_are_included()
    {
        static MessageComponent Menu(string optionLabel) => new ComponentBuilder()
            .WithSelectMenu("menu:1", [
                new SelectMenuOptionBuilder().WithLabel(optionLabel).WithValue("v1")
            ])
            .Build();

        Assert.NotEqual(
            RenderCanonicalizer.Canonicalize(embed: null, Menu("Alpha")),
            RenderCanonicalizer.Canonicalize(embed: null, Menu("Beta")));
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Discord.Tests`
Expected: FAIL to compile — `RenderCanonicalizer` does not exist.

- [ ] **Step 5: Implement `RenderCanonicalizer`**

Write `src/RustPlusBot.Discord/Posting/RenderCanonicalizer.cs`:

```csharp
using System.Globalization;
using System.Text;
using Discord;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Flattens an embed + components render into a deterministic canonical string so identical
///     re-renders can be detected and skipped before hitting Discord's per-channel PATCH bucket.
///     Values are length-prefixed to make adjacent user-controlled strings collision-proof.
/// </summary>
public static class RenderCanonicalizer
{
    /// <summary>Builds the canonical string for a render.</summary>
    /// <param name="embed">The embed about to be sent, or null.</param>
    /// <param name="components">The message components about to be sent, or null.</param>
    /// <returns>A deterministic string that is equal iff the visible render is equal.</returns>
    public static string Canonicalize(Embed? embed, MessageComponent? components)
    {
        var sb = new StringBuilder();
        if (embed is not null)
        {
            AppendEmbed(sb, embed);
        }

        if (components is not null)
        {
            AppendComponents(sb, components);
        }

        return sb.ToString();
    }

    private static void AppendEmbed(StringBuilder sb, Embed embed)
    {
        Append(sb, "title", embed.Title);
        Append(sb, "description", embed.Description);
        Append(sb, "url", embed.Url);
        Append(sb, "color", embed.Color?.RawValue.ToString(CultureInfo.InvariantCulture));
        Append(sb, "timestamp", embed.Timestamp?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        Append(sb, "author.name", embed.Author?.Name);
        Append(sb, "author.url", embed.Author?.Url);
        Append(sb, "author.icon", embed.Author?.IconUrl);
        Append(sb, "footer.text", embed.Footer?.Text);
        Append(sb, "footer.icon", embed.Footer?.IconUrl);
        Append(sb, "thumbnail", embed.Thumbnail?.Url);
        Append(sb, "image", embed.Image?.Url);
        foreach (var field in embed.Fields)
        {
            Append(sb, "field.name", field.Name);
            Append(sb, "field.value", field.Value);
            Append(sb, "field.inline", field.Inline ? "1" : "0");
        }
    }

    private static void AppendComponents(StringBuilder sb, MessageComponent components)
    {
        foreach (var row in components.Components.OfType<ActionRowComponent>())
        {
            Append(sb, "row", null);
            foreach (var component in row.Components)
            {
                AppendComponent(sb, component);
            }
        }
    }

    private static void AppendComponent(StringBuilder sb, IMessageComponent component)
    {
        switch (component)
        {
            case ButtonComponent button:
                Append(sb, "button.id", button.CustomId);
                Append(sb, "button.label", button.Label);
                Append(sb, "button.style", ((int)button.Style).ToString(CultureInfo.InvariantCulture));
                Append(sb, "button.url", button.Url);
                Append(sb, "button.disabled", button.IsDisabled ? "1" : "0");
                Append(sb, "button.emote", button.Emote?.ToString());
                break;
            case SelectMenuComponent menu:
                Append(sb, "menu.id", menu.CustomId);
                Append(sb, "menu.placeholder", menu.Placeholder);
                Append(sb, "menu.min", menu.MinValues.ToString(CultureInfo.InvariantCulture));
                Append(sb, "menu.max", menu.MaxValues.ToString(CultureInfo.InvariantCulture));
                Append(sb, "menu.disabled", menu.IsDisabled ? "1" : "0");
                foreach (var option in menu.Options)
                {
                    Append(sb, "option.label", option.Label);
                    Append(sb, "option.value", option.Value);
                    Append(sb, "option.description", option.Description);
                    Append(sb, "option.default", option.IsDefault == true ? "1" : "0");
                }

                break;
            default:
                // Unknown component kind: its type keeps the canonical string honest (differs from absence).
                Append(sb, "component", component.Type.ToString());
                break;
        }
    }

    private static void Append(StringBuilder sb, string key, string? value)
    {
        sb.Append(key).Append('=');
        if (value is null)
        {
            sb.Append("<null>");
        }
        else
        {
            sb.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
        }

        sb.Append('\n');
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Discord.Tests`
Expected: PASS (10 tests). If `ComponentBuilder.WithSelectMenu(customId, options)` or `SelectMenuOptionBuilder` signatures differ in Discord.Net 3.20, adjust the *test* helper to the actual builder API (the canonicalizer walks the built `SelectMenuComponent`, whose properties are stable).

- [ ] **Step 7: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add tests/RustPlusBot.Discord.Tests src/RustPlusBot.Discord/Posting/RenderCanonicalizer.cs RustPlusBot.slnx
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(discord): add RenderCanonicalizer + RustPlusBot.Discord.Tests project

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `RenderGate`

**Files:**
- Create: `src/RustPlusBot.Discord/Posting/RenderGate.cs`
- Test: `tests/RustPlusBot.Discord.Tests/Posting/RenderGateTests.cs`

**Interfaces:**
- Consumes: nothing (pure; keyed by `ulong messageId` + canonical string from Task 1).
- Produces (Task 3 relies on these exact members):
  - `public sealed class RenderGate`
  - `public bool ShouldSend(ulong messageId, string canonicalRender)` — true when the render differs from the last committed one (or none committed).
  - `public void Commit(ulong messageId, string canonicalRender)` — record a successful send.
  - `public void Invalidate(ulong messageId)` — forget a message (failure or deletion); next render always sends.

- [ ] **Step 1: Write the failing tests**

Write `tests/RustPlusBot.Discord.Tests/Posting/RenderGateTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Discord.Tests`
Expected: FAIL to compile — `RenderGate` does not exist.

- [ ] **Step 3: Implement `RenderGate`**

Write `src/RustPlusBot.Discord/Posting/RenderGate.cs`:

```csharp
using System.Collections.Concurrent;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Per-process memory of the last render successfully sent per Discord message, so identical
///     re-renders (boot primes, periodic contents republishes) skip the PATCH entirely. Entries are
///     committed only after a confirmed send; a failed or deleted message is invalidated so the next
///     render always retries. Restart cost: one edit per message to re-warm the cache — by design.
/// </summary>
public sealed class RenderGate
{
    private readonly ConcurrentDictionary<ulong, string> _lastSent = new();

    /// <summary>Decides whether a render differs from the last committed one for the message.</summary>
    /// <param name="messageId">The Discord message id the render targets.</param>
    /// <param name="canonicalRender">The canonical render string (see <see cref="RenderCanonicalizer"/>).</param>
    /// <returns>True when the render must be sent (differs or nothing committed yet).</returns>
    public bool ShouldSend(ulong messageId, string canonicalRender)
        => !(_lastSent.TryGetValue(messageId, out var last) && last == canonicalRender);

    /// <summary>Records a successfully sent render for the message.</summary>
    /// <param name="messageId">The Discord message id that was posted or edited.</param>
    /// <param name="canonicalRender">The canonical render string that was sent.</param>
    public void Commit(ulong messageId, string canonicalRender) => _lastSent[messageId] = canonicalRender;

    /// <summary>Forgets the message (send failed or message deleted); the next render always sends.</summary>
    /// <param name="messageId">The Discord message id to forget.</param>
    public void Invalidate(ulong messageId) => _lastSent.TryRemove(messageId, out _);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Discord.Tests`
Expected: PASS (16 tests total in the project).

- [ ] **Step 5: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Discord/Posting/RenderGate.cs tests/RustPlusBot.Discord.Tests/Posting/RenderGateTests.cs
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(discord): add RenderGate no-op edit detector

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Convert `DiscordChannelMessenger` to a gated singleton; wire DI + the 5 posters

**Files:**
- Modify: `src/RustPlusBot.Discord/Posting/DiscordChannelMessenger.cs` (full rewrite below — keep the file name; it is Sonar-coverage-excluded by name)
- Modify: `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.Alarms/Posting/DiscordAlarmChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.StorageMonitors/Posting/DiscordStorageMonitorChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.Events/Posting/DiscordEventChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.Players/Posting/DiscordPlayerChannelPoster.cs`

**Interfaces:**
- Consumes: `RenderGate` (Task 2), `RenderCanonicalizer.Canonicalize(Embed?, MessageComponent?)` (Task 1).
- Produces: instance methods with the SAME signatures as today's statics minus the `client` parameter:
  - `Task<ulong?> EnsureAsync(ulong channelId, ulong? messageId, Embed embed, MessageComponent components, ILogger logger, CancellationToken cancellationToken)`
  - `Task PostAsync(ulong channelId, Embed embed, ILogger logger, CancellationToken cancellationToken)`
- No unit tests: the messenger is a Sonar-excluded I/O shim (repo convention); the pure logic was tested in Tasks 1–2. Verification = full solution build + test suite.

- [ ] **Step 1: Rewrite the messenger**

Replace the entire contents of `src/RustPlusBot.Discord/Posting/DiscordChannelMessenger.cs` with:

```csharp
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Discord.Posting;

/// <summary>
///     Shared Discord channel post/edit boilerplate: fetch, options, self-heal, broad-catch — plus a
///     render gate that skips edits whose content is identical to the last successful send, keeping
///     boot primes and periodic republishes out of Discord's per-channel PATCH rate-limit bucket.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="gate">The per-message no-op edit detector.</param>
public sealed class DiscordChannelMessenger(DiscordSocketClient client, RenderGate gate)
{
    /// <summary>Request timeout generous enough to ride out a queued rate-limit burst (default is 15 s).</summary>
    private const int RequestTimeoutMs = 30_000;

    /// <summary>
    ///     Edits the message by id (self-healing on 404 by reposting) or posts a new one.
    ///     Identical re-renders are skipped without calling Discord's edit endpoint.
    ///     Returns the message id, or null on failure.
    /// </summary>
    /// <param name="channelId">The target channel id.</param>
    /// <param name="messageId">The existing message id to edit, or null to post a new one.</param>
    /// <param name="embed">The embed to post or update.</param>
    /// <param name="components">The message components to post or update.</param>
    /// <param name="logger">The caller's logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = CreateOptions(cancellationToken);
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not ITextChannel channel)
            {
                return null;
            }

            var canonical = RenderCanonicalizer.Canonicalize(embed, components);
            if (messageId is { } id)
            {
                // Inner try: some Discord.Net versions THROW (HttpException 404/Unknown Message)
                // rather than return null for a deleted message. Catch it and fall through to repost
                // so the self-heal path always runs.
                try
                {
                    var existing = await channel.GetMessageAsync(id, options: options).ConfigureAwait(false);
                    if (existing is IUserMessage userMessage)
                    {
                        if (!gate.ShouldSend(id, canonical))
                        {
                            // Same content as the last successful send: don't spend the PATCH bucket.
                            return userMessage.Id;
                        }

                        await userMessage.ModifyAsync(m =>
                        {
                            m.Embed = embed;
                            m.Components = components;
                        }, options).ConfigureAwait(false);
                        gate.Commit(id, canonical);
                        return userMessage.Id;
                    }

                    // Message was deleted (returned null / not a user message); fall through to repost.
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Deleted/unknown message; fall through to repost and return the new id.
#pragma warning disable CA1848, CA1873 // Use LoggerMessage delegates / avoid expensive log-arg evaluation — plain logger.Log is fine for a shared helper (no source-gen partial context); ulong boxing is negligible vs. the caught exception.
                    logger.LogDebug(ex, "Embed {MessageId} in channel {ChannelId} was deleted; reposting.", id,
                        channelId);
#pragma warning restore CA1848, CA1873
                }

                // The tracked message is gone; the repost below re-keys the gate under the new id.
                gate.Invalidate(id);
            }

            var posted = await channel
                .SendMessageAsync(embed: embed, options: options, components: components)
                .ConfigureAwait(false);
            gate.Commit(posted.Id, canonical);
            return posted.Id;
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the relay; report failure as null.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (messageId is { } failedId)
            {
                // Outcome unknown (e.g. timeout mid-flight): forget the entry so the next render retries.
                gate.Invalidate(failedId);
            }

#pragma warning disable CA1848, CA1873 // Use LoggerMessage delegates / avoid expensive log-arg evaluation — plain logger.Log is fine for a shared helper (no source-gen partial context); ulong boxing is negligible vs. the caught exception.
            logger.LogWarning(ex, "Posting/editing an embed in channel {ChannelId} failed.", channelId);
#pragma warning restore CA1848, CA1873
            return null;
        }
    }

    /// <summary>Posts an embed fire-and-forget; Discord hiccups are logged and swallowed.</summary>
    /// <param name="channelId">The target channel id.</param>
    /// <param name="embed">The embed to post.</param>
    /// <param name="logger">The caller's logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task PostAsync(
        ulong channelId,
        Embed embed,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = CreateOptions(cancellationToken);
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false) is not ITextChannel channel)
            {
                return;
            }

            await channel.SendMessageAsync(embed: embed, options: options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Cancellation (shutdown) is not a post failure; let the relay loop unwind cleanly.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the relay.
        catch (Exception ex)
#pragma warning restore CA1031
        {
#pragma warning disable CA1848, CA1873 // Use LoggerMessage delegates / avoid expensive log-arg evaluation — plain logger.Log is fine for a shared helper (no source-gen partial context); ulong boxing is negligible vs. the caught exception.
            logger.LogWarning(ex, "Posting an embed to channel {ChannelId} failed.", channelId);
#pragma warning restore CA1848, CA1873
        }
    }

    private static RequestOptions CreateOptions(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken,
        RetryMode = RetryMode.AlwaysRetry,
        Timeout = RequestTimeoutMs,
    };
}
```

- [ ] **Step 2: Register the singletons**

In `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`, add the using and two registrations:

```csharp
using RustPlusBot.Discord.Posting;
```

and inside `AddDiscordBot`, after `services.AddSingleton<IUserDmSender, DiscordUserDmSender>();`:

```csharp
        services.AddSingleton<RenderGate>();
        services.AddSingleton<DiscordChannelMessenger>();
```

- [ ] **Step 3: Convert the 5 posters**

`src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs` — full new contents:

```csharp
using Discord;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Switches.Posting;

/// <summary>Posts/edits switch embeds in #switches by message id. Untested integration shim.</summary>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordSwitchChannelPoster(
    DiscordChannelMessenger messenger,
    ILogger<DiscordSwitchChannelPoster> logger) : ISwitchChannelPoster
{
    /// <inheritdoc />
    public Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        CancellationToken cancellationToken)
        => messenger.EnsureAsync(channelId, messageId, embed, components, logger, cancellationToken);
}
```

Apply the identical transformation to the other four (swap `DiscordSocketClient client` for `DiscordChannelMessenger messenger`, drop the now-unused `Discord.WebSocket` using, call the instance method without `client`):

- `DiscordAlarmChannelPoster.cs` — `EnsureAsync(channelId, messageId, embed, components, logger, cancellationToken)`
- `DiscordStorageMonitorChannelPoster.cs` — `EnsureAsync(channelId, messageId, embed, components, logger, cancellationToken)`
- `DiscordEventChannelPoster.cs` — `PostAsync(channelId, embed, logger, cancellationToken)`
- `DiscordPlayerChannelPoster.cs` — `PostAsync(channelId, embed, logger, cancellationToken)`

- [ ] **Step 4: Build the solution and hunt for stragglers**

Run: `dotnet build -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx`
Expected: any remaining `DiscordChannelMessenger.EnsureAsync(client, ...)` static call sites fail to compile — convert each the same way (grep `DiscordChannelMessenger.` to confirm none remain outside the class itself). Build must end green.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx`
Expected: PASS — posters are substituted in feature tests (`Substitute.For<ISwitchChannelPoster>()` etc.), so nothing observes the internals.

- [ ] **Step 6: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Discord src/RustPlusBot.Features.Switches/Posting src/RustPlusBot.Features.Alarms/Posting src/RustPlusBot.Features.StorageMonitors/Posting src/RustPlusBot.Features.Events/Posting src/RustPlusBot.Features.Players/Posting
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(discord): gate no-op embed edits in DiscordChannelMessenger singleton

Skips PATCHes whose render matches the last successful send; adds
RetryMode.AlwaysRetry + 30s timeout so bursts degrade to slow catch-up.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `ConnectionStatusChangedEvent` gains `IsConnected`/`WasConnected`; supervisor computes them in-process

**Files:**
- Modify: `src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (field + `PublishStatusAsync`, currently around line 937)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` (new test)
- Modify (mechanical ctor fixes): `tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs`, `tests/RustPlusBot.Features.Switches.Tests/Hosting/SwitchesHostedServiceTests.cs`, `tests/RustPlusBot.Features.Alarms.Tests/AlarmStateRelayTests.cs`, `tests/RustPlusBot.Features.Alarms.Tests/Hosting/AlarmsHostedServiceTests.cs`, `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorStateRelayTests.cs`, `tests/RustPlusBot.Features.StorageMonitors.Tests/Hosting/StorageMonitorsHostedServiceTests.cs`, `tests/RustPlusBot.Features.Workspace.Tests/Hosting/WorkspaceConnectionStatusTests.cs`

**Interfaces:**
- Produces (Tasks 5–7 rely on this exact shape):

```csharp
public sealed record ConnectionStatusChangedEvent(
    ulong GuildId,
    Guid ServerId,
    bool IsConnected,
    bool WasConnected);
```

- Relays in this task still use their store-based guard (behavior unchanged); only the event shape and the supervisor change here.

- [ ] **Step 1: Write the failing supervisor test**

Append to `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` (inside the class, after `Heartbeat_Unreachable_ReconnectsAndRecovers`):

```csharp
    /// <summary>
    /// WasConnected must be computed from in-process state (the DB status survives restarts and
    /// would claim Connected at boot): statuses before the first Connected carry false; the drop
    /// after a Connected carries true.
    /// </summary>
    [Fact]
    public async Task StatusEvents_CarryWasConnected_OnlyAfterAConnectedDrop()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(2)); // first heartbeat -> Connected
        source.EnqueueHeartbeat(HeartbeatResult.Unreachable); // next heartbeat -> drop
        source.EnqueueConnect(SocketConnectOutcome.Connected); // reconnect
        source.EnqueueHeartbeat(HeartbeatResult.Ok(4));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var events = new List<ConnectionStatusChangedEvent>();
        _ = Task.Run(async () =>
        {
            await foreach (var e in h.Bus.SubscribeAsync<ConnectionStatusChangedEvent>(cts.Token))
            {
                lock (events)
                {
                    events.Add(e);
                }
            }
        }, cts.Token);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);
        var recovered = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 4);
        Assert.NotNull(recovered);

        // The bus delivers asynchronously; wait until the collector has seen the drop.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (events)
            {
                if (events.Any(e => !e.IsConnected && e.WasConnected))
                {
                    break;
                }
            }

            await Task.Delay(15);
        }

        await cts.CancelAsync();
        ConnectionStatusChangedEvent[] snapshot;
        lock (events)
        {
            snapshot = [.. events];
        }

        var firstConnected = Array.FindIndex(snapshot, e => e.IsConnected);
        Assert.True(firstConnected >= 0, "expected a Connected status event");
        Assert.All(snapshot.Take(firstConnected), e => Assert.False(e.WasConnected));
        var drop = Array.FindIndex(
            snapshot, firstConnected, snapshot.Length - firstConnected, e => !e.IsConnected);
        Assert.True(drop > firstConnected, "expected a drop event after Connected");
        Assert.True(snapshot[drop].WasConnected);
    }
```

- [ ] **Step 2: Run the new test to verify it fails**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.Connections.Tests --filter StatusEvents_CarryWasConnected_OnlyAfterAConnectedDrop`
Expected: FAIL to compile — the record has no `IsConnected`/`WasConnected`.

- [ ] **Step 3: Reshape the event record**

Replace the contents of `src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a server's live-connection state changes, so #info can re-render.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose connection state changed.</param>
/// <param name="IsConnected">True when the new status is Connected.</param>
/// <param name="WasConnected">
///     True when the previous status published in this process was Connected. Deliberately
///     in-process (not store-derived): the persisted status survives restarts and would still
///     read Connected right after boot, re-triggering unreachable sweeps on every startup.
/// </param>
public sealed record ConnectionStatusChangedEvent(
    ulong GuildId,
    Guid ServerId,
    bool IsConnected,
    bool WasConnected);
```

(Bools, not the `ConnectionStatus` enum: `RustPlusBot.Abstractions` is a dependency-free leaf project and the enum lives in `RustPlusBot.Domain`; consumers only need Connected-or-not.)

- [ ] **Step 4: Compute the flags in the supervisor**

In `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`:

Add a field next to the class's other private fields (e.g. near `_liveSockets`); add `using System.Collections.Concurrent;` if not already present:

```csharp
    /// <summary>Last status published per key IN THIS PROCESS — the store's persisted status survives restarts and would falsely report Connected at boot.</summary>
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ConnectionStatus> _publishedStatuses = new();
```

Replace the `if (changed)` block at the end of `PublishStatusAsync` (currently ~line 954):

```csharp
        if (changed)
        {
            var wasConnected = _publishedStatuses.TryGetValue(key, out var previous)
                && previous == ConnectionStatus.Connected;
            _publishedStatuses[key] = status;
            await eventBus.PublishAsync(
                    new ConnectionStatusChangedEvent(key.Guild, key.Server,
                        status == ConnectionStatus.Connected, wasConnected), ct)
                .ConfigureAwait(false);
        }
```

- [ ] **Step 5: Fix every event construction mechanically**

`grep -rn "new ConnectionStatusChangedEvent(" src tests` — besides the supervisor (done in Step 4) there are 11 test sites. Update each, choosing values that preserve the test's intent (relays still guard on the store in this task, so any values compile-and-pass; pick the ones below so Tasks 5–7 need no further edits):

- Sweep-exercising tests (event represents a drop from Connected):
  - `SwitchStateRelayTests.cs` — `ConnectionStatus_not_connected_marks_switches_unreachable` (~line 92) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: true)`
  - `AlarmStateRelayTests.cs` (~line 383) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: true)`
  - `StorageMonitorStateRelayTests.cs` — `HandleConnectionStatusAsync_NotConnected_PostsUnreachable` (~line 117) → `new ConnectionStatusChangedEvent(Guild, Server, IsConnected: false, WasConnected: true)`
  - `SwitchesHostedServiceTests.cs` (~line 155) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: true)`
  - `AlarmsHostedServiceTests.cs` (~line 200) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: true)`
  - `StorageMonitorsHostedServiceTests.cs` (~line 163) → `new ConnectionStatusChangedEvent(Guild, serverId, IsConnected: false, WasConnected: true)`
- Connected-does-nothing tests (event represents a healthy connection):
  - `SwitchStateRelayTests.cs` — `ConnectionStatus_connected_does_nothing` (~line 110) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: true, WasConnected: true)`
  - `AlarmStateRelayTests.cs` (~line 405) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: true, WasConnected: true)`
  - `StorageMonitorStateRelayTests.cs` (~line 135) → `new ConnectionStatusChangedEvent(Guild, Server, IsConnected: true, WasConnected: true)`
- Indifferent consumer (Workspace reconciles regardless of status):
  - `WorkspaceConnectionStatusTests.cs` (~line 35) → `new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: false)`

- [ ] **Step 6: Run the full suite**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx`
Expected: PASS, including the new supervisor test (relay behavior is unchanged in this task — the relays ignore the new fields until Tasks 5–7).

- [ ] **Step 7: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs tests
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(connections): carry IsConnected/WasConnected on ConnectionStatusChangedEvent

WasConnected is computed from in-process published history, not the
persisted store (which would still say Connected right after boot).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: `SwitchStateRelay` sweeps only on a drop from Connected

**Files:**
- Modify: `src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs` (`HandleConnectionStatusAsync`, currently lines 122–159)
- Test: `tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs`

**Interfaces:**
- Consumes: `ConnectionStatusChangedEvent.IsConnected` / `.WasConnected` (Task 4).
- Produces: no signature changes; `HandleConnectionStatusAsync` drops its `IConnectionStore` read.

- [ ] **Step 1: Write the failing boot-no-sweep test**

Add to `tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs` after `ConnectionStatus_connected_does_nothing`:

```csharp
    /// <summary>Boot/reconnect-loop statuses (never Connected in this process) must not sweep — embeds keep their last-run state until the prime republishes.</summary>
    [Fact]
    public async Task ConnectionStatus_boot_without_prior_connection_does_not_sweep()
    {
        var h = Create();
        var serverId = Guid.NewGuid();
        h.Store.ListByServerAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartSwitch
                {
                    GuildId = 10UL,
                    ServerId = serverId,
                    EntityId = 42UL,
                    Name = "G",
                    MessageId = 900UL
                }
            ]);

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: false),
            CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.Switches.Tests --filter ConnectionStatus_boot_without_prior_connection_does_not_sweep`
Expected: FAIL — the current store-based guard sees no Connected state (the substitute returns null) and sweeps, so `EnsureAsync` IS received.

- [ ] **Step 3: Swap the guard**

In `src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs`, replace `HandleConnectionStatusAsync` with:

```csharp
    /// <summary>Handles a connection-status change: a drop from Connected marks its switch embeds unreachable.</summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every affected embed has been re-rendered.</returns>
    public async Task HandleConnectionStatusAsync(
        ConnectionStatusChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.IsConnected || !evt.WasConnected)
        {
            // Connected: the supervisor's prime path republishes real state — nothing to do.
            // Never-connected in this process (boot, reconnect-loop repeats): keep the last-run
            // embeds; only a drop from Connected sweeps them to unreachable.
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            var switches = await store.ListByServerAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (switches.Count == 0)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var sw in switches)
            {
                await RenderAsync(store, sw, isActive: null, evt.GuildId, evt.ServerId, culture, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
```

Remove the now-unused usings the compiler flags (expected: `RustPlusBot.Domain.Connections` and `RustPlusBot.Persistence.Connections` — verify nothing else in the file uses them before deleting).

- [ ] **Step 4: Clean the existing tests of dead store stubbing**

In `SwitchStateRelayTests.cs`:
- `ConnectionStatus_not_connected_marks_switches_unreachable`: delete the `h.Connections.GetStateAsync(...).Returns(new ConnectionState {...});` stub (the guard no longer reads the store).
- `ConnectionStatus_connected_does_nothing`: delete its `h.Connections...` stub likewise.
- Run `grep -n "h.Connections" tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs` — if no references remain, remove the `IConnectionStore` substitute from `Create()`, its `services.AddScoped(_ => connections);` registration, the `Connections` member of the `Harness` record, and now-unused usings (`RustPlusBot.Persistence.Connections`, `RustPlusBot.Domain.Connections` — again only if genuinely unused).
- In `tests/RustPlusBot.Features.Switches.Tests/Hosting/SwitchesHostedServiceTests.cs`, the sweep test's `h.Connections.GetStateAsync(...)` stub is likewise dead — remove it the same way (keep the substitute itself if other tests or the harness DI need it).

- [ ] **Step 5: Run the feature's tests**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.Switches.Tests`
Expected: PASS — new boot test green, both existing sweep tests green with `WasConnected: true` values set in Task 4.

- [ ] **Step 6: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Features.Switches tests/RustPlusBot.Features.Switches.Tests
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(switches): sweep embeds unreachable only on a drop from Connected

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: `AlarmStateRelay` sweeps only on a drop from Connected

**Files:**
- Modify: `src/RustPlusBot.Features.Alarms/Relaying/AlarmStateRelay.cs` (`HandleConnectionStatusAsync`, currently lines 114–136)
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmStateRelayTests.cs`

**Interfaces:**
- Consumes: `ConnectionStatusChangedEvent.IsConnected` / `.WasConnected` (Task 4); `IAlarmRefresher.RefreshAsync(SmartAlarm, bool unreachable, CancellationToken)` (existing).
- Produces: no signature changes; handler drops its `IConnectionStore` read.

- [ ] **Step 1: Write the failing boot-no-sweep test**

Add to `tests/RustPlusBot.Features.Alarms.Tests/AlarmStateRelayTests.cs` after `ConnectionStatus_connected_does_nothing`:

```csharp
    /// <summary>Boot/reconnect-loop statuses (never Connected in this process) must not sweep — embeds keep their last-run state until the prime republishes.</summary>
    [Fact]
    public async Task ConnectionStatus_boot_without_prior_connection_does_not_sweep()
    {
        var serverId = Guid.NewGuid();
        var h = Create();

        h.Store.ListByServerAsync(10UL, serverId, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartAlarm
                {
                    GuildId = 10UL, ServerId = serverId, EntityId = 42UL, Name = "A"
                },
            ]);

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: false, WasConnected: false),
            CancellationToken.None);

        await h.Refresher.DidNotReceive()
            .RefreshAsync(Arg.Any<SmartAlarm>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.Alarms.Tests --filter ConnectionStatus_boot_without_prior_connection_does_not_sweep`
Expected: FAIL — the store-based guard sweeps and `RefreshAsync` IS received.

- [ ] **Step 3: Swap the guard**

In `src/RustPlusBot.Features.Alarms/Relaying/AlarmStateRelay.cs`, replace `HandleConnectionStatusAsync` with (keep the method's existing XML doc summary style, updated to the new behavior):

```csharp
    /// <summary>Handles a connection-status change: a drop from Connected marks its alarm embeds unreachable.</summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when every affected embed has been refreshed.</returns>
    public async Task HandleConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.IsConnected || !evt.WasConnected)
        {
            // Connected: the supervisor's prime path republishes real state — nothing to do.
            // Never-connected in this process (boot, reconnect-loop repeats): keep the last-run
            // embeds; only a drop from Connected sweeps them to unreachable.
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            var alarms = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var alarm in alarms)
            {
                // Reuse the already-loaded alarm rather than re-fetching each by id.
                await refresher.RefreshAsync(alarm, unreachable: true, ct).ConfigureAwait(false);
            }
        }
    }
```

Match the original parameter name (`ct` vs `cancellationToken`) to whatever the file currently uses. Remove now-unused usings the compiler flags.

- [ ] **Step 4: Clean the existing tests of dead store stubbing**

In `AlarmStateRelayTests.cs`, delete the `h.Connections.GetStateAsync(...)` stubs from the two existing `ConnectionStatus_*` tests; if `grep -n "h.Connections" AlarmStateRelayTests.cs` shows no remaining references, remove the substitute from the harness as in Task 5 Step 4. Same cleanup in `tests/RustPlusBot.Features.Alarms.Tests/Hosting/AlarmsHostedServiceTests.cs` for its sweep test.

- [ ] **Step 5: Run the feature's tests**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.Alarms.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Features.Alarms tests/RustPlusBot.Features.Alarms.Tests
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(alarms): sweep embeds unreachable only on a drop from Connected

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: `StorageMonitorStateRelay` sweeps only on a drop from Connected

**Files:**
- Modify: `src/RustPlusBot.Features.StorageMonitors/Relaying/StorageMonitorStateRelay.cs` (`HandleConnectionStatusAsync`, currently lines 105–138)
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorStateRelayTests.cs`

**Interfaces:**
- Consumes: `ConnectionStatusChangedEvent.IsConnected` / `.WasConnected` (Task 4).
- Produces: no signature changes; handler drops its `IConnectionStore` read.

- [ ] **Step 1: Write the failing boot-no-sweep test**

Add to `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorStateRelayTests.cs` after `HandleConnectionStatusAsync_Connected_DoesNothing` (this file uses `Guild`/`Server` constants):

```csharp
    /// <summary>Boot/reconnect-loop statuses (never Connected in this process) must not sweep — embeds keep their last-run state until the prime republishes.</summary>
    [Fact]
    public async Task HandleConnectionStatusAsync_BootWithoutPriorConnection_DoesNothing()
    {
        var h = Create();
        h.Store.ListByServerAsync(Guild, Server, Arg.Any<CancellationToken>())
            .Returns(
            [
                new SmartStorageMonitor
                {
                    GuildId = Guild,
                    ServerId = Server,
                    EntityId = 7UL,
                    Name = "TC",
                    MessageId = null,
                },
            ]);

        await h.Relay.HandleConnectionStatusAsync(
            new ConnectionStatusChangedEvent(Guild, Server, IsConnected: false, WasConnected: false),
            CancellationToken.None);

        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.StorageMonitors.Tests --filter HandleConnectionStatusAsync_BootWithoutPriorConnection_DoesNothing`
Expected: FAIL — the store-based guard sweeps and `EnsureAsync` IS received.

- [ ] **Step 3: Swap the guard**

In `src/RustPlusBot.Features.StorageMonitors/Relaying/StorageMonitorStateRelay.cs`, replace `HandleConnectionStatusAsync` with:

```csharp
    /// <summary>Handles a connection-status change: a drop from Connected marks its storage embeds unreachable.</summary>
    /// <param name="evt">The connection-status change.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when every affected embed has been re-rendered.</returns>
    public async Task HandleConnectionStatusAsync(
        ConnectionStatusChangedEvent evt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.IsConnected || !evt.WasConnected)
        {
            // Connected: the supervisor's prime path republishes real state — nothing to do.
            // Never-connected in this process (boot, reconnect-loop repeats): keep the last-run
            // embeds; only a drop from Connected sweeps them to unreachable.
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            var monitors = await store.ListByServerAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (monitors.Count == 0)
            {
                return;
            }

            var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var monitor in monitors)
            {
                await RenderAsync(store, monitor, contents: null, evt.GuildId, evt.ServerId, culture,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
```

Remove now-unused usings the compiler flags.

- [ ] **Step 4: Clean the existing tests of dead store stubbing**

Same recipe as Tasks 5–6: delete the `h.Connections.GetStateAsync(...)` stubs from `HandleConnectionStatusAsync_NotConnected_PostsUnreachable` and `HandleConnectionStatusAsync_Connected_DoesNothing`; drop the substitute from the harness if `h.Connections` has no remaining references; same cleanup in `tests/RustPlusBot.Features.StorageMonitors.Tests/Hosting/StorageMonitorsHostedServiceTests.cs`.

- [ ] **Step 5: Run the feature's tests**

Run: `dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/tests/RustPlusBot.Features.StorageMonitors.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Features.StorageMonitors tests/RustPlusBot.Features.StorageMonitors.Tests
git -C /home/handys11/Dev/RustPlusBot commit -m "feat(storage): sweep embeds unreachable only on a drop from Connected

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Move command registration off the gateway Ready handler

**Files:**
- Modify: `src/RustPlusBot.Discord/DiscordBotService.cs` (`OnReadyAsync`, currently lines 62–85)

**Interfaces:**
- Consumes: nothing new. Produces: no public changes. `DiscordBotService.cs` is Sonar-coverage-excluded (Discord I/O); no unit test — verification is the solution build + the disappearance of the `Ready handler is blocking the gateway task` log warning (Task 9).

- [ ] **Step 1: Rewrite `OnReadyAsync`**

Replace `OnReadyAsync` in `src/RustPlusBot.Discord/DiscordBotService.cs` with:

```csharp
    private Task OnReadyAsync()
    {
        // Ready fires on every gateway (re)connect; only register commands once per process.
        // Ready is dispatched serially on the gateway thread, so no synchronization is needed —
        // set the flag before offloading so a re-fired Ready can't double-register.
        if (_hasRegisteredCommands)
        {
            return Task.CompletedTask;
        }

        _hasRegisteredCommands = true;

        // Registration is REST work (one call per guild); doing it inline blocks the gateway task
        // and stalls event dispatch, so offload it. Failures must be caught here — nothing awaits this.
        _ = Task.Run(RegisterCommandsAsync);
        return Task.CompletedTask;
    }

    private async Task RegisterCommandsAsync()
    {
        try
        {
            if (_options.ResetCommandsOnStartup)
            {
                await client.Rest.DeleteAllGlobalCommandsAsync().ConfigureAwait(false);
                logger.LogWarning(
                    "ResetCommandsOnStartup is enabled: deleted all global application commands before registration.");
            }

            foreach (var guild in client.Guilds)
            {
                await interactions.RegisterCommandsToGuildAsync(guild.Id).ConfigureAwait(false);
            }

            logger.LogInformation("Registered commands to {GuildCount} guild(s).", client.Guilds.Count);
        }
#pragma warning disable CA1031 // Broad catch: fire-and-forget — an unobserved exception would vanish; log it instead.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Registering slash commands failed.");
        }
    }
```

- [ ] **Step 2: Build and run the full suite**

Run: `dotnet build -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx && dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx`
Expected: build green, all tests PASS.

- [ ] **Step 3: Commit**

```bash
git -C /home/handys11/Dev/RustPlusBot add src/RustPlusBot.Discord/DiscordBotService.cs
git -C /home/handys11/Dev/RustPlusBot commit -m "fix(discord): register slash commands off the gateway Ready handler

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Gates + live boot verification

**Files:**
- None expected (format fixes only, if the cleanup tool reorders anything).

- [ ] **Step 1: Full build + test**

Run: `dotnet build -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx && dotnet test -maxcpucount:1 /home/handys11/Dev/RustPlusBot/RustPlusBot.slnx`
Expected: green; total test count ≥ 818 + the ~20 added by this plan.

- [ ] **Step 2: Format gate**

```bash
cd /home/handys11/Dev/RustPlusBot
dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR
git status --porcelain
```

Expected: empty `git status` output. If files changed, review the diff, re-run the tests, and commit the formatting as `style: jb cleanupcode` (with the co-author trailer).

- [ ] **Step 3: Live boot verification (uses the `verify` skill's spirit — drive the real flow)**

Launch the host the way the user normally runs it (`dotnet run --project src/RustPlusBot.Host`), let it connect, then inspect today's log file `src/RustPlusBot.Host/logs/rustplusbot-<yyyyMMdd>.log`:

- ZERO `Rate limit triggered` warnings during the first minute after boot.
- NO `A Ready handler is blocking the gateway task` warning.
- NO `TimeoutException` from `DiscordChannelMessenger`.
- In Discord: device embeds did NOT flicker to unreachable at boot; toggling a switch still edits its embed immediately; deleting a device embed and triggering its device reposts it (self-heal intact).
- Stop the bot, restart it: at most one edit per device message (cold cache), still zero rate-limit warnings.

If the environment can't run the bot (no Discord token / Rust server), state that plainly in the summary and list these checks as pending manual verification for the user.

- [ ] **Step 4: Report**

Summarize: tests added/passing, gates green, live checklist results. Do NOT push or open a PR without the user's go-ahead.
