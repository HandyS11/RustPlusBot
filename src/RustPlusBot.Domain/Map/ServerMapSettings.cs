using Persistord.Core.Abstractions;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Domain.Map;

/// <summary>Per-(guild, server) rendered-map layer settings; one row per server. Layers default on.</summary>
public sealed class ServerMapSettings : IGuildScoped
{
    /// <summary>The server id (FK to RustServer; primary key, one row per server).</summary>
    public Guid ServerId { get; set; }

    /// <summary>Whether the grid layer is drawn.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>Whether live cargo/heli/chinook markers are drawn.</summary>
    public bool ShowMarkers { get; set; } = true;

    /// <summary>Whether monument icons are drawn.</summary>
    public bool ShowMonuments { get; set; } = true;

    /// <summary>Whether the travelling-vendor marker is drawn.</summary>
    public bool ShowVendor { get; set; } = true;

    /// <summary>Whether teammate position markers are drawn.</summary>
    public bool ShowPlayers { get; set; } = true;

    /// <summary>Whether oil rigs are styled by activation state.</summary>
    public bool ShowRigs { get; set; } = true;

    /// <summary>Whether train-tunnel entrance icons are drawn (separate from monuments).</summary>
    public bool ShowTunnels { get; set; } = true;

    /// <summary>Which grid convention the rendered map and event grid references use.</summary>
    public MapGridStyle GridStyle { get; set; } = MapGridStyle.InGame;

    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }
}
