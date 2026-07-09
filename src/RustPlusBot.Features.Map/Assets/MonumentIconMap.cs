namespace RustPlusBot.Features.Map.Assets;

/// <summary>Maps a Rust+ monument protobuf token to a vendored icon key (filename without extension). Unknown tokens return null and are skipped.</summary>
public static class MonumentIconMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["AbandonedMilitaryBase"] = "militarybase",
        ["airfield_display_name"] = "airfield",
        ["arctic_base_a"] = "arcticresearch",
        ["bandit_camp"] = "banditcamp",
        ["dome_monument_name"] = "dome",
        ["excavator"] = "excavator",
        ["ferryterminal"] = "ferryterminal",
        ["fishing_village_display_name"] = "fishingvillage",
        ["large_fishing_village_display_name"] = "fishingvillagelarge",
        ["gas_station"] = "gasstation",
        ["harbor_display_name"] = "harbour",
        ["harbor_2_display_name"] = "harbour2",
        ["junkyard_display_name"] = "junkyard",
        ["large_oil_rig"] = "largeoilrig",
        ["oil_rig_small"] = "oilrig",
        ["launchsite"] = "launchsite",
        ["lighthouse_display_name"] = "lighthouse",
        ["military_tunnels_display_name"] = "militarytunnel",
        ["mining_outpost_display_name"] = "miningoutpost",
        ["mining_quarry_hqm_display_name"] = "hqmquarry",
        ["mining_quarry_stone_display_name"] = "stonequarry",
        ["mining_quarry_sulfur_display_name"] = "sulfurquarry",
        ["missile_silo_monument"] = "missilesilo",
        ["outpost"] = "outpost",
        ["power_plant_display_name"] = "powerplant",
        ["satellite_dish_display_name"] = "satellitedish",
        ["sewer_display_name"] = "sewerbranch",
        ["stables_a"] = "stable",
        ["stables_b"] = "stable",
        ["supermarket"] = "supermarket",
        ["swamp_c"] = "swamp",
        ["train_yard_display_name"] = "trainyard",
        ["train_tunnel_display_name"] = "traintunnel",
        ["water_treatment_plant_display_name"] = "watertreatment",
    };

    /// <summary>Gets the icon key for a monument token, or null when unmapped.</summary>
    /// <param name="token">The Rust+ monument protobuf token.</param>
    /// <returns>The icon key (filename without extension), or null.</returns>
    public static string? IconKeyFor(string token) =>
        token is not null && Map.TryGetValue(token, out var key) ? key : null;
}
