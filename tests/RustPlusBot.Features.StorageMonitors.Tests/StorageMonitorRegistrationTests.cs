using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.StorageMonitors;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Relaying;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class StorageMonitorRegistrationTests
{
    [Fact]
    public void AddStorageMonitors_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddStorageMonitors();

        Assert.Contains(services, d => d.ServiceType == typeof(StorageMonitorPairingCoordinator));
        Assert.Contains(services, d => d.ServiceType == typeof(StorageMonitorStateRelay));
        Assert.Contains(services, d => d.ServiceType == typeof(StorageMonitorEmbedRenderer));
        Assert.Contains(services, d => d.ServiceType == typeof(IItemNameResolver));
        Assert.Contains(services, d => d.ServiceType == typeof(ILocalizer));
    }
}
