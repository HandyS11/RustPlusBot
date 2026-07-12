using NSubstitute;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Features.Pairing.Notifications;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class DiscordOwnerNotifierTests
{
    [Fact]
    public async Task NotifyCredentialsExpired_DmsTheOwner()
    {
        var dm = Substitute.For<IUserDmSender>();
        var notifier = new DiscordOwnerNotifier(dm);

        await notifier.NotifyCredentialsExpiredAsync(10UL, 99UL);

        await dm.Received(1).SendAsync(
            99UL,
            Arg.Is<string>(m => m.Contains("Reconnect", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifySetupChannelMissing_dms_owner_with_setup_instructions()
    {
        var dm = Substitute.For<IUserDmSender>();
        var notifier = new DiscordOwnerNotifier(dm);

        await notifier.NotifySetupChannelMissingAsync(10UL, 99UL, CancellationToken.None);

        await dm.Received(1).SendAsync(99UL,
            Arg.Is<string>(m => m.Contains("/setup", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }
}
