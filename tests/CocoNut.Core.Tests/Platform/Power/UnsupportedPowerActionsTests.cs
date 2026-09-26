using CocoNut.Core.Abstractions;
using CocoNut.Platform.Power;

namespace CocoNut.Core.Tests.Platform.Power;

public class UnsupportedPowerActionsTests
{
    [Theory]
    [InlineData(StopAction.Shutdown)]
    [InlineData(StopAction.Suspend)]
    [InlineData(StopAction.Hibernate)]
    public void IsSupported_AlwaysFalse(StopAction action) => Assert.False(new UnsupportedPowerActions().IsSupported(action));

    [Fact]
    public async Task ExecuteAsync_ThrowsPlatformNotSupportedException() =>
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new UnsupportedPowerActions().ExecuteAsync(StopAction.Shutdown));
}
