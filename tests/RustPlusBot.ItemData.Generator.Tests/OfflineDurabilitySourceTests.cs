using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineDurabilitySourceTests
{
    private static OfflineDurabilitySource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineDurabilitySource(path);
    }

    [Fact]
    public void LoadRaidTargets_KeepsExplosiveOnly_AndProjectsAllThreeKinds()
    {
        const string json = """
                            {
                              "items": { "100": [
                                {"group":"explosive","which":null,"toolId":"1248356124","caption":null,"quantity":1,"time":10,"fuel":60,"sulfur":2200},
                                {"group":"guns","which":null,"toolId":"99","caption":"AK","quantity":500,"time":120,"fuel":null,"sulfur":null}
                              ]},
                              "buildingBlocks": { "Stone Wall": [
                                {"group":"explosive","which":"soft","toolId":"1248356124","caption":null,"quantity":2,"time":11.5,"fuel":120,"sulfur":4400}
                              ]},
                              "other": { "Bradley APC": [
                                {"group":"explosive","which":null,"toolId":"-742865266","caption":null,"quantity":7,"time":30,"fuel":null,"sulfur":9800}
                              ]}
                            }
                            """;
        var names = new Dictionary<int, string>
        {
            [100] = "Tool Cupboard", [1248356124] = "Timed Explosive Charge", [-742865266] = "Rocket",
        };

        var targets = SourceWith(json).LoadRaidTargets(names);

        var tc = Assert.Single(targets, t => t.Kind == RaidTargetKind.Item);
        Assert.Equal("Tool Cupboard", tc.Name);
        Assert.Equal("100", tc.Key);
        var cost = Assert.Single(tc.Costs); // the "guns" row is filtered out
        Assert.Equal(1248356124, cost.ToolId);
        Assert.Equal(2200, cost.Sulfur);

        var wall = Assert.Single(targets, t => t.Kind == RaidTargetKind.BuildingBlock);
        Assert.Equal("Stone Wall", wall.Name);
        Assert.Equal("soft", Assert.Single(wall.Costs).Side);

        Assert.Single(targets, t => t.Kind == RaidTargetKind.Vehicle);
    }

    [Fact]
    public void LoadRaidTargets_DropsItemWithNoName()
    {
        const string json =
            """{"items":{"999":[{"group":"explosive","which":null,"caption":null,"toolId":"1","quantity":1,"time":1,"sulfur":1,"fuel":null}]}}""";
        var targets = SourceWith(json).LoadRaidTargets(new Dictionary<int, string>());
        Assert.Empty(targets);
    }

    [Fact]
    public void LoadRaidTargets_DropsTargetWithNoExplosiveRows()
    {
        const string json =
            """{"buildingBlocks":{"Twig Wall":[{"group":"melee","which":null,"caption":null,"toolId":"1","quantity":1,"time":1,"sulfur":null,"fuel":null}]}}""";
        var targets = SourceWith(json).LoadRaidTargets(new Dictionary<int, string>());
        Assert.Empty(targets);
    }
}
