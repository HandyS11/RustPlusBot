using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Switches.Pairing;
using RustPlusBot.Features.Switches.Relaying;
using RustPlusBot.Features.Switches.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchRegistrationTests
{
    [Fact]
    public void AddSwitches_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddSwitches();

        Assert.Contains(services, d => d.ServiceType == typeof(SwitchPairingCoordinator));
        Assert.Contains(services, d => d.ServiceType == typeof(SwitchStateRelay));
        Assert.Contains(services, d => d.ServiceType == typeof(SwitchEmbedRenderer));
        Assert.Contains(services, d => d.ServiceType == typeof(ILocalizer));
    }
}
