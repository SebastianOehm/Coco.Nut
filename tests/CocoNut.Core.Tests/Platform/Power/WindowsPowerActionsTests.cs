using CocoNut.Core.Abstractions;
using CocoNut.Platform.Power;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Core.Tests.Platform.Power;

public class WindowsPowerActionsTests
{
    [Fact]
    public async Task ExecuteAsync_Shutdown_RunsShutdownExeWithForceImmediateFlags()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty));
        var actions = CreateActions(processRunner: processRunner);

        await actions.ExecuteAsync(StopAction.Shutdown);

        Assert.Equal(1, processRunner.CallCount);
        Assert.Equal("shutdown.exe", Path.GetFileName(processRunner.FileName));
        Assert.Equal(["/s", "/f", "/t", "0"], processRunner.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_Shutdown_NonZeroExitCode_ThrowsPowerActionException()
    {
        var processRunner = new FakeProcessRunner(new ProcessResult(1, string.Empty, "access denied"));
        var actions = CreateActions(processRunner: processRunner);

        var exception = await Assert.ThrowsAsync<PowerActionException>(() => actions.ExecuteAsync(StopAction.Shutdown));
        Assert.Contains("access denied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Suspend_CallsNativeSetSuspendStateWithWinNutFlags()
    {
        var native = new FakeWindowsPowerNative();
        var actions = CreateActions(native: native);

        await actions.ExecuteAsync(StopAction.Suspend);

        Assert.Equal(1, native.CallCount);
        Assert.False(native.LastHibernate);
        Assert.False(native.LastForceCritical);
        Assert.True(native.LastDisableWakeEvent);
    }

    [Fact]
    public async Task ExecuteAsync_Hibernate_CallsNativeSetSuspendStateWithHibernateTrue()
    {
        var native = new FakeWindowsPowerNative();
        var actions = CreateActions(native: native);

        await actions.ExecuteAsync(StopAction.Hibernate);

        Assert.Equal(1, native.CallCount);
        Assert.True(native.LastHibernate);
        Assert.False(native.LastForceCritical);
        Assert.True(native.LastDisableWakeEvent);
    }

    [Fact]
    public async Task ExecuteAsync_SetSuspendStateReturnsFalse_ThrowsPowerActionException()
    {
        var native = new FakeWindowsPowerNative { SetSuspendStateResult = false };
        var actions = CreateActions(native: native);

        await Assert.ThrowsAsync<PowerActionException>(() => actions.ExecuteAsync(StopAction.Suspend));
    }

    [Fact]
    public async Task ExecuteAsync_HibernateNotSupported_ThrowsPlatformNotSupportedException()
    {
        var native = new FakeWindowsPowerNative { HibernateSupported = false };
        var actions = CreateActions(native: native);

        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => actions.ExecuteAsync(StopAction.Hibernate));
    }

    [Fact]
    public void IsSupported_ShutdownAndSuspend_AlwaysTrue()
    {
        var actions = CreateActions();

        Assert.True(actions.IsSupported(StopAction.Shutdown));
        Assert.True(actions.IsSupported(StopAction.Suspend));
    }

    [Fact]
    public void IsSupported_Hibernate_ReflectsNative()
    {
        var native = new FakeWindowsPowerNative { HibernateSupported = false };
        var actions = CreateActions(native: native);

        Assert.False(actions.IsSupported(StopAction.Hibernate));
    }

    private static WindowsPowerActions CreateActions(FakeWindowsPowerNative? native = null, FakeProcessRunner? processRunner = null) =>
        new(
            native ?? new FakeWindowsPowerNative(),
            processRunner ?? new FakeProcessRunner(new ProcessResult(0, string.Empty, string.Empty)),
            NullLogger<WindowsPowerActions>.Instance);
}
