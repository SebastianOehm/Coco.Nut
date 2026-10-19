using System.Diagnostics;
using CocoNut.Core.Actions;

namespace CocoNut.Core.Tests.Actions;

/// <summary>Real-process tests; they only use programs every CI platform has (the dotnet host, <c>sleep</c>/<c>ping</c>, <c>sh</c>).</summary>
public class ProcessCommandRunnerTests
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>();

    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    /// <summary>The dotnet host that runs these tests; its path is exposed by the CLI, else the one on PATH is used.</summary>
    private static string? DotnetHost()
    {
        var fromCli = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrEmpty(fromCli) && File.Exists(fromCli))
        {
            return fromCli;
        }

        var processPath = Environment.ProcessPath;
        return processPath is not null && Path.GetFileNameWithoutExtension(processPath) == "dotnet" ? processPath : null;
    }

    [Fact]
    public async Task RealProcess_ExitCodeZero_IsReported()
    {
        if (DotnetHost() is not { } dotnet)
        {
            return; // no dotnet host to run in this environment.
        }

        var result = await new ProcessCommandRunner().RunAsync(
            new CommandRequest(dotnet, ["--version"], NoEnvironment, Generous), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task RealProcess_NonZeroExit_PassesEnvironmentAndCapturesStandardError()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // relies on /bin/sh; the Windows shell quoting would only test cmd.exe.
        }

        var environment = new Dictionary<string, string> { ["COCONUT_RULE"] = "my rule" };

        var result = await new ProcessCommandRunner().RunAsync(
            new CommandRequest("sh", ["-c", "echo \"rule=$COCONUT_RULE; arg=$0\" >&2; exit 3", "a b"], environment, Generous),
            CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal("rule=my rule; arg=a b", result.StandardError.Trim());
    }

    [Fact]
    public async Task RealProcess_ThatOutlivesItsTimeout_IsKilled()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("ping", new[] { "-n", "60", "127.0.0.1" })
            : ("sleep", new[] { "60" });
        var stopwatch = Stopwatch.StartNew();

        var result = await new ProcessCommandRunner().RunAsync(
            new CommandRequest(fileName, arguments, NoEnvironment, TimeSpan.FromMilliseconds(300)), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), "the process must be killed, not waited for");
    }

    [Fact]
    public async Task RealProcess_Cancellation_KillsItAndThrows()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("ping", new[] { "-n", "60", "127.0.0.1" })
            : ("sleep", new[] { "60" });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessCommandRunner().RunAsync(
            new CommandRequest(fileName, arguments, NoEnvironment, Generous), cts.Token));
    }

    [Fact]
    public async Task RealProcess_ThatDoesNotExist_Throws()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => new ProcessCommandRunner().RunAsync(
            new CommandRequest("coconut-no-such-program-xyz", [], NoEnvironment, Generous), CancellationToken.None));
    }
}
