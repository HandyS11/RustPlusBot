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
        names.Resolve(Arg.Any<int>()).Returns(ci => "Item" + (int)ci[0]);
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
