using CocoNut.App.Services;
using CocoNut.Core.Settings;

namespace CocoNut.App.Tests.Services;

public sealed class VelopackAppUpdaterTests
{
    [Fact]
    public void Cannot_self_update_when_not_running_from_a_Velopack_install()
    {
        var updater = new VelopackAppUpdater(() => UpdateChannel.Stable);

        Assert.False(updater.CanSelfUpdate);
    }

    [Fact]
    public void Constructor_rejects_a_missing_channel_provider() =>
        Assert.Throws<ArgumentNullException>(() => new VelopackAppUpdater(null!));
}
