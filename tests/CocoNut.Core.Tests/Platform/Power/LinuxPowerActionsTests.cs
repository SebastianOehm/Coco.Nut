using CocoNut.Core.Abstractions;
using CocoNut.Platform.Power;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Core.Tests.Platform.Power;

public class LinuxPowerActionsTests
{
    [Theory]
    [InlineData(StopAction.Shutdown, "poweroff")]
    [InlineData(StopAction.Suspend, "suspend")]
    [InlineData(StopAction.Hibernate, "hibernate")]
    public async Task ExecuteAsync_RunsSystemctlWithExpectedArgument(StopAction action, string expectedArgument)
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var actions = CreateActions(processRunner: processRunner);

        await actions.ExecuteAsync(action);

        Assert.Equal(1, processRunner.CallCount);
        Assert.Equal("systemctl", processRunner.FileName);
        Assert.Equal([expectedArgument], processRunner.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_ThrowsPowerActionException()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(1, string.Empty, "not authorized"));
        var actions = CreateActions(processRunner: processRunner);

        var exception = await Assert.ThrowsAsync<PowerActionException>(() => actions.ExecuteAsync(StopAction.Suspend));
        Assert.Contains("not authorized", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_SystemctlMissing_ThrowsPlatformNotSupportedExceptionAndNeverRunsProcess()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var executableLocator = new FakeExecutableLocator { Exists = false };
        var actions = CreateActions(processRunner: processRunner, executableLocator: executableLocator);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => actions.ExecuteAsync(StopAction.Shutdown));

        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public void IsSupported_ChecksExecutableLocatorForSystemctl()
    {
        var executableLocator = new FakeExecutableLocator { Exists = true };
        var actions = CreateActions(executableLocator: executableLocator);

        Assert.True(actions.IsSupported(StopAction.Shutdown));
        Assert.Equal("systemctl", executableLocator.LastRequested);
    }

    private static LinuxPowerActions CreateActions(FakeProcessRunner? processRunner = null, FakeExecutableLocator? executableLocator = null) =>
        new(
            processRunner ?? new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty)),
            executableLocator ?? new FakeExecutableLocator(),
            NullLogger<LinuxPowerActions>.Instance);
}
