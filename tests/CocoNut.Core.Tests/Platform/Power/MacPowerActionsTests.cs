using CocoNut.Core.Abstractions;
using CocoNut.Platform.Power;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Core.Tests.Platform.Power;

public class MacPowerActionsTests
{
    [Fact]
    public async Task ExecuteAsync_Shutdown_RunsOsascriptShutdownCommand()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var actions = CreateActions(processRunner);

        await actions.ExecuteAsync(StopAction.Shutdown);

        Assert.Equal("osascript", processRunner.FileName);
        Assert.Equal(["-e", "tell application \"System Events\" to shut down"], processRunner.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_Suspend_RunsPmsetSleepnow()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var actions = CreateActions(processRunner);

        await actions.ExecuteAsync(StopAction.Suspend);

        Assert.Equal("pmset", processRunner.FileName);
        Assert.Equal(["sleepnow"], processRunner.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_ThrowsPowerActionException()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(1, string.Empty, "boom"));
        var actions = CreateActions(processRunner);

        var exception = await Assert.ThrowsAsync<PowerActionException>(() => actions.ExecuteAsync(StopAction.Suspend));
        Assert.Contains("boom", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Hibernate_ThrowsPlatformNotSupportedExceptionAndNeverRunsProcess()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var actions = CreateActions(processRunner);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => actions.ExecuteAsync(StopAction.Hibernate));

        Assert.Equal(0, processRunner.CallCount);
    }

    [Fact]
    public void IsSupported_Hibernate_IsFalse_ShutdownAndSuspend_AreTrue()
    {
        var actions = CreateActions(new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty)));

        Assert.True(actions.IsSupported(StopAction.Shutdown));
        Assert.True(actions.IsSupported(StopAction.Suspend));
        Assert.False(actions.IsSupported(StopAction.Hibernate));
    }

    private static MacPowerActions CreateActions(FakeProcessRunner processRunner) =>
        new(processRunner, NullLogger<MacPowerActions>.Instance);
}
