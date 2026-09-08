using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>
/// Attachments are uploads, not text: the reconciler re-renders every pass, so the file must be sent once
/// and only re-sent when it actually changes — while the words around it stay editable in place.
/// </summary>
public sealed class WorkspaceReconcilerAttachmentTests
{
    private static AttachmentHolder Holder(string fileName) =>
        new(new MessageAttachment([1, 2, 3], fileName));

    private static AttachmentHolder EmbeddedHolder(string fileName) =>
        new(new MessageAttachment([1, 2, 3], fileName), shownInEmbed: true);

    [Fact]
    public async Task Unchanged_attachment_is_never_re_uploaded()
    {
        var holder = Holder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.PostedMessages);
    }

    [Fact]
    public async Task Changed_attachment_is_reposted_with_the_new_file()
    {
        var holder = Holder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        // A new wipe: the server serves a different map, so the renderer names the file after new content.
        holder.Current = new MessageAttachment([9, 9, 9], "map-b.jpg");
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.PostedMessages);
        Assert.Equal(0, harness.Gateway.EditedMessages);
        Assert.Equal("map-b.jpg", harness.Gateway.PostedPayloads[^1].Attachment!.FileName);
    }

    [Fact]
    public async Task Dropping_the_attachment_reposts_rather_than_leaving_the_stale_upload()
    {
        // Editing cannot remove an upload, so a message that stops carrying one has to be reposted —
        // otherwise the old image keeps showing next to the new embed.
        var holder = Holder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        holder.Current = null;
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.PostedMessages);
        Assert.Equal(0, harness.Gateway.EditedMessages);
        Assert.Null(harness.Gateway.PostedPayloads[^1].Attachment);
    }

    [Fact]
    public async Task A_deleted_attachment_message_is_reposted()
    {
        var holder = Holder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var message = await harness.Store.GetMessageAsync(1, null, "information.map");
        harness.Gateway.ExternallyDeleteMessage(message!.DiscordMessageId);
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.PostedMessages);
    }

    [Fact]
    public async Task An_upload_shown_inside_the_embed_is_still_recognised_as_the_same_file()
    {
        // Discord folds an attachment:// upload into the embed and reports an EMPTY attachments array, so
        // the file name has to be read back off the embed's CDN URL. Miss that and the reconciler decides
        // the image changed on every pass and re-uploads it — which is what the live #info map did.
        var holder = EmbeddedHolder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.PostedMessages);
    }

    [Fact]
    public async Task Text_around_an_unchanged_upload_is_still_edited_in_place()
    {
        // The upload is the only part an edit cannot carry. Everything else still has to follow the
        // renderer — a guild switching culture would otherwise keep its map embed in the old language for
        // as long as the map itself does not change, which is the whole wipe.
        var holder = EmbeddedHolder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        holder.Title = "carte";
        await sut.ReconcileGlobalAsync(1);

        var live = await harness.Store.GetMessageAsync(1, null, "information.map");
        Assert.Equal(1, harness.Gateway.PostedMessages); // no repost: the file did not change
        Assert.Equal(1, harness.Gateway.EditedMessages);
        Assert.Equal("carte", harness.Gateway.LivePayload(live!.DiscordMessageId)!.Embed!.Title);
    }

    [Fact]
    public async Task An_upload_shown_inside_the_embed_is_reposted_when_the_image_changes()
    {
        var holder = EmbeddedHolder("map-a.jpg");
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithAttachmentMessage(WorkspaceScope.Global, "information.map", "information", holder);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        holder.Current = new MessageAttachment([9, 9, 9], "map-b.jpg");
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.PostedMessages);
        Assert.Equal("map-b.jpg", harness.Gateway.PostedPayloads[^1].Attachment!.FileName);
    }
}
