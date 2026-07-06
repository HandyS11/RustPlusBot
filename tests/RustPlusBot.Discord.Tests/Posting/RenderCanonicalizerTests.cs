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
