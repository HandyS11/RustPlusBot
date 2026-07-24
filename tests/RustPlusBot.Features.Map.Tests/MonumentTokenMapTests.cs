using RustMapsApi.V4.Assets;
using RustMapsApi.V4.Models;
using RustPlusBot.Features.Map.Assets;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MonumentTokenMapTests
{
    [Theory]
    [InlineData("airfield_display_name", MonumentType.Airfield)]
    [InlineData("dome_monument_name", MonumentType.SphereTank)]
    [InlineData("mining_outpost_display_name", MonumentType.Warehouse)]
    [InlineData("bandit_camp", MonumentType.BanditTown)]
    [InlineData("radtown", MonumentType.Radtown)]
    [InlineData("jungle_ziggurat", MonumentType.JungleZigguratA)]
    [InlineData("apartmentcomplex", MonumentType.ApartmentsComplex)]
    [InlineData("train_tunnel_display_name", MonumentType.TunnelEntrance)]
    [InlineData("train_tunnel_link_display_name", MonumentType.TunnelEntranceTransition)]
    [InlineData("arctic_base_b", MonumentType.ArcticResearchBaseA)]
    [InlineData("stables_a", MonumentType.StablesA)]
    public void TypeFor_maps_known_tokens(string token, MonumentType expected) =>
        Assert.Equal(expected, MonumentTokenMap.TypeFor(token));

    [Fact]
    public void TypeFor_maps_rig_tokens()
    {
        Assert.Equal(MonumentType.OilrigSmall, MonumentTokenMap.TypeFor("oil_rig_small"));
        Assert.Equal(MonumentType.OilrigLarge, MonumentTokenMap.TypeFor("large_oil_rig"));
    }

    [Theory]
    [InlineData("swamp_a")]
    [InlineData("swamp_b")]
    [InlineData("swamp_c")]
    public void TypeFor_matches_swamps_by_prefix(string token) =>
        Assert.Equal(MonumentType.SwampC, MonumentTokenMap.TypeFor(token));

    [Theory]
    [InlineData("underwater_lab")]
    [InlineData("underwater_lab_d")]
    public void TypeFor_matches_underwater_labs_by_prefix(string token) =>
        Assert.Equal(MonumentType.UnderwaterA, MonumentTokenMap.TypeFor(token));

    [Theory]
    [InlineData("assets/bundled/prefabs/autospawn/monument/underwater_lab/underwater_lab_d.prefab",
        MonumentType.UnderwaterA)]
    [InlineData("assets/bundled/prefabs/autospawn/monument/medium/swamp_a.prefab", MonumentType.SwampC)]
    public void TypeFor_maps_path_qualified_prefab_tokens(string token, MonumentType expected) =>
        Assert.Equal(expected, MonumentTokenMap.TypeFor(token));

    [Theory]
    [InlineData("definitely_not_a_monument")]
    [InlineData("assets/bundled/prefabs/autospawn/underwater-lab-base/module_900x900_2way_moonpool.prefab")]
    [InlineData("")]
    [InlineData(null)]
    public void TypeFor_returns_null_for_unknown_or_empty(string? token) =>
        Assert.Null(MonumentTokenMap.TypeFor(token));

    [Theory]
    [InlineData("assets/bundled/prefabs/autospawn/underwater-lab-base/module_900x900_2way_moonpool.prefab", true)]
    [InlineData("assets/bundled/prefabs/autospawn/underwater-lab-base/module_1200x1200_a.prefab", true)]
    [InlineData("assets/bundled/prefabs/autospawn/monument/underwater_lab/underwater_lab_d.prefab", false)]
    [InlineData("definitely_not_a_monument", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsIgnored_flags_only_underwater_lab_interior_modules(string? token, bool expected) =>
        Assert.Equal(expected, MonumentTokenMap.IsIgnored(token));

    [Fact]
    public void Every_mapped_type_has_a_package_asset()
    {
        // Drift guard: fails loud if a RustMapsApi.Assets update drops art we depend on.
        // Includes two prefix-family samples (swamp_a, underwater_lab_d) alongside KnownTokens so a
        // dropped Swamp or Underwater_Lab asset fails loud too, not just the exactly-mapped tokens.
        foreach (var token in MonumentTokenMap.KnownTokens.Concat(["swamp_a", "underwater_lab_d"]))
        {
            var type = MonumentTokenMap.TypeFor(token);
            Assert.NotNull(type);
            Assert.True(MonumentAssets.HasAsset(type.Value),
                $"{token} → {type} has no asset in RustMapsApi.Assets");
        }
    }
}
