using CocoNut.Platform.Processes;

namespace CocoNut.Core.Tests.Platform.Processes;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_CapturesExitCodeAndStandardStreams()
    {
        var runner = new ProcessRunner();
        var (fileName, arguments) = ShellExitCommand(7, "hello");

        var result = await runner.RunAsync(fileName, arguments);

        Assert.Equal(7, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.Contains("hello", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ZeroExitCode_Succeeds()
    {
        var runner = new ProcessRunner();
        var (fileName, arguments) = ShellExitCommand(0, "ok");

        var result = await runner.RunAsync(fileName, arguments);

        Assert.True(result.Succeeded);
    }

    /// <summary>
    /// A tiny, side-effect-free command (just prints and exits with a chosen code) that exists on every CI
    /// runner we build for (ubuntu-latest, windows-latest) - never a power action.
    /// </summary>
    private static (string FileName, string[] Arguments) ShellExitCommand(int exitCode, string echoText) =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", $"echo {echoText} & exit {exitCode}"])
            : ("/bin/sh", ["-c", $"echo {echoText}; exit {exitCode}"]);
}
