using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>
/// Attachments are uploads, not text: the reconciler re-renders every pass, so a message carrying one must
/// be posted once and then left alone, and only re-posted when the file itself changes.
/// </summary>
public sealed class WorkspaceReconcilerAttachmentTests
{
    private static AttachmentHolder Holder(string fileName) =>
        new(new MessageAttachment([1, 2, 3], fileName));

    private static AttachmentHolder EmbeddedHolder(string fileName) =>
        new(new MessageAttachment([1, 2, 3], fileName), shownInEmbed: true);

    [Fact]
    public async Task Unchanged_attachment_is_neither_re_uploaded_nor_edited()
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
        Assert.Equal(0, harness.Gateway.EditedMessages);
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
    public async Task An_upload_shown_inside_the_embed_is_still_recognised_as_up_to_date()
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
        Assert.Equal(0, harness.Gateway.EditedMessages);
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
