using RustPlusBot.Features.Workspace.Gateway;

namespace RustPlusBot.Features.Workspace.Tests.Gateway;

/// <summary>
/// Regression: an embed that shows its upload through <c>attachment://</c> makes Discord return an EMPTY
/// attachments array, so reading only that array reports "no attachment" for precisely the messages that
/// have one — and the reconciler then re-uploaded the map on every single pass.
/// </summary>
public sealed class LiveMessageTests
{
    private const string CdnUrl =
        "https://cdn.discordapp.com/attachments/1546525916438732821/1546666488394682369/server-map-0bcdf958d810.jpg"
        + "?ex=6aa09cea&is=6a9f4b6a&hm=1d280d9d4c581707192d7ce7ac5807c938bfb15afcf5f311aeaa968ab4be129c&";

    [Fact]
    public void Name_is_recovered_from_the_embed_image_when_the_attachment_list_is_empty()
    {
        var live = LiveMessage.From(1, attachmentFileName: null, CdnUrl);

        Assert.Equal("server-map-0bcdf958d810.jpg", live.AttachmentFileName);
    }

    [Fact]
    public void The_rotating_cdn_signature_is_not_part_of_the_name()
    {
        // Discord re-signs CDN links on every read; leaving the query string in would make an unchanged
        // image look like a new one on every pass — the very churn this guards against.
        var first = LiveMessage.From(1, null, CdnUrl);
        var later = LiveMessage.From(1, null, CdnUrl.Replace("ex=6aa09cea", "ex=6bb1adfb", StringComparison.Ordinal));

        Assert.Equal(first.AttachmentFileName, later.AttachmentFileName);
    }

    [Fact]
    public void The_attachment_list_wins_when_it_has_an_entry()
    {
        var live = LiveMessage.From(1, "listed.jpg", CdnUrl);

        Assert.Equal("listed.jpg", live.AttachmentFileName);
    }

    [Fact]
    public void A_non_attachment_embed_image_is_not_a_file_name()
    {
        // A verified RustMaps render is a plain hosted image, not an upload; reading a name out of it would
        // make the reconciler believe an upload is present.
        var live = LiveMessage.From(1, null, "https://content.rustmaps.com/maps/288/c6fe4c21/map_icons.png");

        Assert.Null(live.AttachmentFileName);
    }

    [Fact]
    public void A_message_with_no_image_has_no_file_name()
    {
        Assert.Null(LiveMessage.From(1, null, null).AttachmentFileName);
    }
}
