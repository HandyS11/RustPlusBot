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

    /// <summary>Indicates whether a token is a known non-monument that should be skipped silently.</summary>
    /// <param name="token">The Rust+ monument protobuf token (or prefab name).</param>
    /// <returns>True when the token is deliberately iconless; false otherwise.</returns>
    /// <remarks>
    /// Underwater labs are assembled from interior module prefabs (moonpools, corridors, …) that
    /// Rust+ lists alongside the lab itself; only the lab warrants an icon, so the module family is
    /// skipped without the unknown-token log.
    /// </remarks>
    public static bool IsIgnored(string? token) =>
        token?.Contains("underwater-lab-base/", StringComparison.Ordinal) == true;

    /// <summary>Gets the RustMaps monument type for a Rust+ token, or null when unmapped.</summary>
    /// <param name="token">The Rust+ monument protobuf token (or prefab name).</param>
    /// <returns>The monument type whose asset represents the token, or null.</returns>
    public static MonumentType? TypeFor(string? token)
    {
        var name = PrefabName(token);
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (Map.TryGetValue(name, out var type))
        {
            return type;
        }

        // Rust+ sends prefab names (not fixed tokens) for these families.
        if (name.StartsWith("swamp", StringComparison.Ordinal))
        {
            return MonumentType.SwampC;
        }

        return name.StartsWith("underwater_lab", StringComparison.Ordinal) ? MonumentType.UnderwaterA : null;
    }

    /// <summary>Reduces a path-qualified prefab token ("assets/.../swamp_a.prefab") to its bare name.</summary>
    /// <param name="token">The raw Rust+ token: a bare display-name token or a full prefab path.</param>
    /// <returns>The last path segment with any ".prefab" suffix removed; bare tokens come back unchanged.</returns>
    private static string? PrefabName(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return token;
        }

        var name = token[(token.LastIndexOf('/') + 1)..];
        return name.EndsWith(".prefab", StringComparison.Ordinal) ? name[..^".prefab".Length] : name;
    }
}
