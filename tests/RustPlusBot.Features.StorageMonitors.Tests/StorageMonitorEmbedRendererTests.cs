using Discord;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class StorageMonitorEmbedRendererTests
{
    private static StorageMonitorEmbedRenderer Create(out IItemNameResolver names)
    {
        var loc = new ResxLocalizer();
        names = Substitute.For<IItemNameResolver>();
        names.Resolve(Arg.Any<int>()).Returns(ci => "Item" + (int)ci[0]!);
        return new StorageMonitorEmbedRenderer(loc, names);
    }

    private static SmartStorageMonitor Sample(string name = "Box") => new()
    {
        Id = Guid.NewGuid(), ServerId = Guid.NewGuid(), EntityId = 7UL, Name = name,
    };

    [Fact]
    public void RenderMonitor_NullContents_ShowsUnreachableAndDisablesButtons()
    {
        var renderer = Create(out _);
        var monitor = Sample("Box");

        var (embed, components) = renderer.RenderMonitor(monitor, contents: null, culture: "en");

        Assert.Contains("Unreachable", embed.Description ?? string.Empty, StringComparison.Ordinal);
        var buttons = components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components)
            .OfType<ButtonComponent>()
            .ToList();
        Assert.All(buttons, b => Assert.True(b.IsDisabled));
    }

    [Fact]
    public void RenderMonitor_ToolCupboardWithProtection_ShowsTypeAndProtectionText()
    {
        var renderer = Create(out _);
        var monitor = Sample("TC");
        var contents = new StorageContentsSnapshot(24, true, DateTimeOffset.UtcNow.AddHours(4), []);

        var (embed, _) = renderer.RenderMonitor(monitor, contents, "en");

        Assert.Contains("Tool Cupboard", embed.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("Protected", embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Pins the rendered protection countdown at the day, hour and minute boundaries. The renderer
    ///     computes the remaining span against the wall clock, so each expiry is set far enough inside
    ///     its bucket that a slow test run cannot tip it into the next one.
    /// </summary>
    /// <param name="offsetSeconds">Seconds from now until protection expires.</param>
    /// <param name="expected">The compact duration the embed must show.</param>
    [Theory]
    [InlineData((25 * 3600) + 1800, "1d 1h")] // over a day: days + leftover hours
    [InlineData((24 * 3600) + 1800, "1d 0h")] // exactly on the day boundary
    [InlineData(5400 + 30, "1h 30m")] // 90 minutes: hours + leftover minutes
    [InlineData(3600 + 30, "1h 0m")] // exactly on the hour boundary
    [InlineData(45, "0m")] // under a minute truncates to zero minutes
    [InlineData(-3600, "0m")] // already expired clamps to zero
    public void RenderMonitor_ProtectionRemaining_UsesCompactDuration(int offsetSeconds, string expected)
    {
        var renderer = Create(out _);
        var contents = new StorageContentsSnapshot(
            24, true, DateTimeOffset.UtcNow.AddSeconds(offsetSeconds), []);

        var (embed, _) = renderer.RenderMonitor(Sample("TC"), contents, "en");

        Assert.Contains(expected, embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMonitor_LargeBoxWithItems_ListsItemsSortedDescAndNoProtection()
    {
        var renderer = Create(out _);
        var monitor = Sample("LargeBox");
        var contents = new StorageContentsSnapshot(48, null, null,
        [
            new StorageItemSnapshot(100, 5, false),
            new StorageItemSnapshot(200, 50, false),
        ]);

        var (embed, _) = renderer.RenderMonitor(monitor, contents, "en");

        var desc = embed.Description ?? string.Empty;
        // Items should appear; IItemNameResolver returns "Item{id}"
        Assert.Contains("Item200", desc, StringComparison.Ordinal);
        Assert.Contains("Large Box", desc, StringComparison.Ordinal);
        // No protection line for a non-TC box
        Assert.DoesNotContain("Protected", desc, StringComparison.Ordinal);
        Assert.DoesNotContain("protected", desc, StringComparison.Ordinal);
        // Item200 (qty 50) should appear before Item100 (qty 5)
        Assert.True(desc.IndexOf("Item200", StringComparison.Ordinal) <
                    desc.IndexOf("Item100", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderMonitor_EmptyBox_ShowsEmptyText()
    {
        var renderer = Create(out _);
        var monitor = Sample("EmptyBox");
        var contents = new StorageContentsSnapshot(12, null, null, []);

        var (embed, _) = renderer.RenderMonitor(monitor, contents, "en");

        var desc = embed.Description ?? string.Empty;
        Assert.Contains("Empty", desc, StringComparison.Ordinal);
        Assert.Contains("Small Box", desc, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeviceReachability.Removed, "Removed in-game")]
    [InlineData(DeviceReachability.NoPrivilege, "No building privilege")]
    [InlineData(DeviceReachability.NoResponse, "No response")]
    public void RenderMonitor_NonReachable_ShowsReasonStatus(DeviceReachability reachability, string expectedText)
    {
        var renderer = Create(out _);
        var monitor = new SmartStorageMonitor
        {
            Id = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            EntityId = 7UL,
            Name = "Box",
            Reachability = reachability,
        };

        var (embed, _) = renderer.RenderMonitor(monitor, contents: null, culture: "en");

        Assert.Contains(expectedText, embed.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DeviceReachability.Removed)]
    [InlineData(DeviceReachability.NoPrivilege)]
    public void RenderMonitor_BlockedReachability_DisablesButtons(DeviceReachability reachability)
    {
        var renderer = Create(out _);
        var monitor = new SmartStorageMonitor
        {
            Id = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            EntityId = 7UL,
            Name = "Box",
            Reachability = reachability,
        };

        var (_, components) = renderer.RenderMonitor(monitor, contents: null, culture: "en");

        var buttons = components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components)
            .OfType<ButtonComponent>()
            .ToList();
        Assert.All(buttons, b => Assert.True(b.IsDisabled));
    }

    [Fact]
    public void RenderMonitor_NoResponse_KeepsButtonsEnabled()
    {
        var renderer = Create(out _);
        var monitor = new SmartStorageMonitor
        {
            Id = Guid.NewGuid(),
            ServerId = Guid.NewGuid(),
            EntityId = 7UL,
            Name = "Box",
            Reachability = DeviceReachability.NoResponse,
        };

        var (_, components) = renderer.RenderMonitor(monitor, contents: null, culture: "en");

        var buttons = components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components)
            .OfType<ButtonComponent>()
            .ToList();
        Assert.False(buttons.All(b => b.IsDisabled));
    }

    [Fact]
    public void RenderPrompt_HasAcceptAndDismissButtons()
    {
        var renderer = Create(out _);
        var serverId = Guid.NewGuid();

        var (embed, components) = renderer.RenderPrompt(serverId, 42UL, "Storage Monitor 42", "en");

        Assert.Contains("New storage monitor detected", embed.Title ?? string.Empty, StringComparison.Ordinal);
        var buttons = components.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components)
            .OfType<ButtonComponent>()
            .ToList();
        Assert.Contains(buttons, b =>
            b.CustomId == $"{StorageMonitorComponentIds.AcceptPrefix}{serverId}:42");
        Assert.Contains(buttons, b =>
            b.CustomId == $"{StorageMonitorComponentIds.DismissPrefix}{serverId}:42");
    }
}
