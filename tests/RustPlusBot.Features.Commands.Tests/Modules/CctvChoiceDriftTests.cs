using System.Reflection;
using Discord.Interactions;
using RustPlusBot.Features.Commands.Modules;
using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.Commands.Tests.Modules;

public sealed class CctvChoiceDriftTests
{
    private static IReadOnlyList<string> ChoiceValues()
    {
        var parameter = typeof(ItemCommandModule)
            .GetMethod(nameof(ItemCommandModule.CctvAsync))!
            .GetParameters()[0];
        return
        [
            ..parameter.GetCustomAttributes<ChoiceAttribute>()
                .Select(c => (string)c.Value!)
        ];
    }

    [Fact]
    public void EveryChoiceResolvesToAMonument()
    {
        var db = new EmbeddedItemDatabase();
        foreach (var value in ChoiceValues())
        {
            Assert.IsType<RustPlusBot.Features.ItemData.Lookup.CctvMatch.Found>(db.ResolveCctv(value));
        }
    }

    [Fact]
    public void ChoiceSetEqualsMonumentSet()
    {
        var db = new EmbeddedItemDatabase();
        var monuments = db.CctvMonuments.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var choices = ChoiceValues().ToHashSet(StringComparer.Ordinal);
        Assert.Equal(monuments, choices);
    }
}
