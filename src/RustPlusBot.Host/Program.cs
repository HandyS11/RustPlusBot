using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord;
using RustPlusBot.Host.Credentials;
using RustPlusBot.Persistence;

var builder = Host.CreateApplicationBuilder(args);

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

var host = builder.Build();

// Apply migrations at startup using a short-lived scope (the scoped BotDbContext is disposed
// synchronously by the scope, so no await-using is needed here).
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
    await db.Database.MigrateAsync().ConfigureAwait(false);
}

await host.RunAsync().ConfigureAwait(false);
