using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord;
using RustPlusBot.Features.Alarms;
using RustPlusBot.Features.Chat;
using RustPlusBot.Features.Commands;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Events;
using RustPlusBot.Features.Map;
using RustPlusBot.Features.Pairing;
using RustPlusBot.Features.Players;
using RustPlusBot.Features.StorageMonitors;
using RustPlusBot.Features.Switches;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Host.Credentials;
using RustPlusBot.Persistence;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection("Discord"))
    .Validate(static o => !string.IsNullOrWhiteSpace(o.Token), "Discord:Token is required.")
    .ValidateOnStart();

var connectionString = builder.Configuration["Database:ConnectionString"] ?? "DataSource=rustplusbot.db";

builder.Services.AddDataProtection();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();
builder.Services.AddBotPersistence(connectionString);
builder.Services.AddDiscordBot();
builder.Services.AddOptions<WorkspaceOptions>()
    .Bind(builder.Configuration.GetSection("Workspace"));
builder.Services.AddWorkspace();
builder.Services.AddOptions<PairingOptions>()
    .Bind(builder.Configuration.GetSection("Pairing"))
    .Validate(static o => o.ProbeTimeout > TimeSpan.Zero, "Pairing:ProbeTimeout must be positive.")
    .Validate(static o => o.InitialRetryDelay > TimeSpan.Zero, "Pairing:InitialRetryDelay must be positive.")
    .Validate(static o => o.MaxRetryDelay >= o.InitialRetryDelay,
        "Pairing:MaxRetryDelay must be at least InitialRetryDelay.")
    .ValidateOnStart();
builder.Services.AddPairing();
builder.Services.AddOptions<ConnectionOptions>()
    .Bind(builder.Configuration.GetSection("Connections"))
    .Validate(static o => o.ConnectTimeout > TimeSpan.Zero, "Connections:ConnectTimeout must be positive.")
    .Validate(static o => o.InitialRetryDelay > TimeSpan.Zero, "Connections:InitialRetryDelay must be positive.")
    .Validate(static o => o.MaxRetryDelay >= o.InitialRetryDelay,
        "Connections:MaxRetryDelay must be at least InitialRetryDelay.")
    .Validate(static o => o.HeartbeatInterval > TimeSpan.Zero, "Connections:HeartbeatInterval must be positive.")
    .Validate(static o => o.HeartbeatTimeout > TimeSpan.Zero, "Connections:HeartbeatTimeout must be positive.")
    .Validate(static o => o.HeartbeatTimeout < o.HeartbeatInterval,
        "Connections:HeartbeatTimeout must be less than HeartbeatInterval.")
    .Validate(static o => o.MarkerPollInterval > TimeSpan.Zero, "Connections:MarkerPollInterval must be positive.")
    .Validate(static o => o.MarkerPollFastInterval > TimeSpan.Zero,
        "Connections:MarkerPollFastInterval must be positive.")
    .Validate(static o => o.RigRadius > 0f, "Connections:RigRadius must be positive.")
    .Validate(static o => o.RigActiveWindow > TimeSpan.Zero, "Connections:RigActiveWindow must be positive.")
    .Validate(static o => o.RigOfflineWindow > TimeSpan.Zero, "Connections:RigOfflineWindow must be positive.")
    .Validate(static o => o.RigTickInterval > TimeSpan.Zero, "Connections:RigTickInterval must be positive.")
    .ValidateOnStart();
builder.Services.AddConnections();
builder.Services.AddChat();
builder.Services.AddOptions<CommandOptions>()
    .Bind(builder.Configuration.GetSection("Commands"))
    .Validate(static o => o.Cooldown > TimeSpan.Zero, "Commands:Cooldown must be positive.")
    .ValidateOnStart();
builder.Services.AddCommands();
builder.Services.AddOptions<MaintenanceOptions>()
    .Bind(builder.Configuration.GetSection("Workspace"));
builder.Services.AddEvents();
builder.Services.AddPlayers();
builder.Services.AddOptions<MapOptions>()
    .Bind(builder.Configuration.GetSection("Map"))
    .Validate(static o => o.MapRefreshInterval > TimeSpan.Zero, "Map:MapRefreshInterval must be positive.")
    .ValidateOnStart();
builder.Services.AddMap();
builder.Services.AddSwitches();
builder.Services.AddAlarms();
builder.Services.AddStorageMonitors();

var host = builder.Build();

// Apply migrations at startup using a short-lived scope (the scoped BotDbContext is disposed
// synchronously by the scope, so no await-using is needed here).
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

await host.RunAsync().ConfigureAwait(false);
