using RustMapsApi.V4.Models;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Maps a Rust+ monument protobuf token to the RustMaps <see cref="MonumentType"/> whose icon
/// represents it. Unknown tokens return null; the caller decides how to report them.
/// </summary>
/// <remarks>
/// Token list sourced from the Rust+ app protocol as catalogued by the previous render map plus the
/// rustplusplus and rustplus.py bot projects. Swamps and underwater labs have no fixed token — Rust+
/// sends prefab names — so those two families match by prefix. Several <see cref="MonumentType"/>
/// values share one asset (e.g. both harbors → Harbor); where the exact variant is unknowable from
/// the token, the pick is arbitrary but fixed.
/// </remarks>
public static class MonumentTokenMap
{
    private static readonly Dictionary<string, MonumentType> Map = new(StringComparer.Ordinal)
    {
        ["AbandonedMilitaryBase"] = MonumentType.MilitaryBaseA,
        ["airfield_display_name"] = MonumentType.Airfield,
        ["arctic_base_a"] = MonumentType.ArcticResearchBaseA,
        ["arctic_base_b"] = MonumentType.ArcticResearchBaseA,
        ["bandit_camp"] = MonumentType.BanditTown,
        ["dome_monument_name"] = MonumentType.SphereTank,
        ["excavator"] = MonumentType.Excavator,
        ["ferryterminal"] = MonumentType.FerryTerminal1,
        ["fishing_village_display_name"] = MonumentType.FishingVillageA,
        ["large_fishing_village_display_name"] = MonumentType.FishingVillageB,
        ["gas_station"] = MonumentType.Gasstation,
        ["harbor_display_name"] = MonumentType.HarborLarge,
        ["harbor_2_display_name"] = MonumentType.HarborSmall,
        ["jungle_ziggurat"] = MonumentType.JungleZigguratA,
        ["junkyard_display_name"] = MonumentType.Junkyard,
        ["large_oil_rig"] = MonumentType.OilrigLarge,
        ["oil_rig_small"] = MonumentType.OilrigSmall,
        ["launchsite"] = MonumentType.LaunchSite,
        ["lighthouse_display_name"] = MonumentType.Lighthouse,
        ["military_tunnels_display_name"] = MonumentType.MilitaryTunnels,
        ["mining_outpost_display_name"] = MonumentType.Warehouse,
        ["mining_quarry_hqm_display_name"] = MonumentType.HqmQuarry,
        ["mining_quarry_stone_display_name"] = MonumentType.StoneQuarry,
        ["mining_quarry_sulfur_display_name"] = MonumentType.SulfurQuarry,
        ["missile_silo_monument"] = MonumentType.NuclearMissileSilo,
        ["outpost"] = MonumentType.Outpost,
        ["power_plant_display_name"] = MonumentType.Powerplant,
        ["radtown"] = MonumentType.Radtown,
        ["satellite_dish_display_name"] = MonumentType.SatelliteDish,
        ["sewer_display_name"] = MonumentType.SewerBranch,
        ["stables_a"] = MonumentType.StablesA,
        ["stables_b"] = MonumentType.StablesB,
        ["supermarket"] = MonumentType.Supermarket,
        ["train_tunnel_display_name"] = MonumentType.TunnelEntrance,
        ["train_tunnel_link_display_name"] = MonumentType.TunnelEntranceTransition,
        ["train_yard_display_name"] = MonumentType.Trainyard,
        ["water_treatment_plant_display_name"] = MonumentType.WaterTreatment,
    };

    /// <summary>All exactly-mapped tokens (excludes the prefix families); exposed for drift-guard tests.</summary>
    internal static IReadOnlyCollection<string> KnownTokens => Map.Keys;

    /// <summary>Gets the RustMaps monument type for a Rust+ token, or null when unmapped.</summary>
    /// <param name="token">The Rust+ monument protobuf token (or prefab name).</param>
    /// <returns>The monument type whose asset represents the token, or null.</returns>
    public static MonumentType? TypeFor(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        if (Map.TryGetValue(token, out var type))
        {
            return type;
        }

        // Rust+ sends prefab names (not fixed tokens) for these families.
        if (token.StartsWith("swamp", StringComparison.Ordinal))
        {
            return MonumentType.SwampC;
        }

        return token.StartsWith("underwater_lab", StringComparison.Ordinal) ? MonumentType.UnderwaterA : null;
    }
}
