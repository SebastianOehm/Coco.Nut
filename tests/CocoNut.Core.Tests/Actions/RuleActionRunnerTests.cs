using System.Globalization;
using System.Net;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Tests.TestSupport;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Actions;

public class RuleActionRunnerTests
{
    private sealed class FakeNotifications : INotificationService
    {
        public List<(string Title, string Message, NotificationKind Kind)> Shown { get; } = [];

        public Exception? Throw { get; set; }

        public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            lock (Shown)
            {
                Shown.Add((title, message, kind));
            }
        }
    }

    private sealed class FakeCommandRunner : ICommandRunner
    {
        public List<CommandRequest> Requests { get; } = [];

        public Func<CommandRequest, CancellationToken, Task<CommandResult>> Behavior { get; set; } =
            (_, _) => Task.FromResult(new CommandResult(0, false, string.Empty));

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return Behavior(request, cancellationToken);
        }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(Uri? Uri, string Body)> Requests { get; } = [];

        public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add((request.RequestUri, body));
            }

            return await Respond(cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public FakeNotifications Notifications { get; } = new();
        public FakeCommandRunner Commands { get; } = new();
        public FakeHandler Handler { get; } = new();
        public CapturingLogger<RuleActionRunner> Logger { get; } = new();
        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        public RuleActionRunner Runner { get; }

        public Fixture()
        {
            Runner = new RuleActionRunner(Notifications, Commands, new HttpClient(Handler), Time, Logger);
        }

        public void Dispose() => Runner.Dispose();
    }

    private static readonly UpsReading Reading = new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Status = UpsStatus.OB,
        RawStatus = "OB",
        BatteryCharge = 12.5,
        BatteryRuntime = TimeSpan.FromSeconds(300),
    };

    private static RuleMatch Match(params RuleAction[] actions) =>
        new(new ActionRule { Name = "Test rule", Actions = [.. actions] }, "detail");

    [Fact]
    public async Task Notify_ShowsTheExpandedMessage_WithTheRuleNameAsTitle_InTheCurrentCulture()
    {
        using var f = new Fixture();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            await f.Runner.RunAsync(Match(RuleAction.Notify("{ups} at {charge}%")), Reading, "myups");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var (title, message, kind) = Assert.Single(f.Notifications.Shown);
        Assert.Equal("Test rule", title);
        Assert.Equal("myups at 12,5%", message);
        Assert.Equal(NotificationKind.Info, kind);
    }

    [Theory]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public async Task Log_WritesTheExpandedMessage_AtTheConfiguredLevel_WithTheInvariantCulture(LogLevel level)
    {
        using var f = new Fixture();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            await f.Runner.RunAsync(Match(RuleAction.Log("charge {charge}", level)), Reading, "myups");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var entry = Assert.Single(f.Logger.Entries);
        Assert.Equal(level, entry.Level);
        Assert.Equal("Rule 'Test rule': charge 12.5", entry.Message);
    }

    [Fact]
    public async Task RunCommand_PassesCommandArgumentsEnvironmentAndTimeout()
    {
        using var f = new Fixture();

        await f.Runner.RunAsync(Match(RuleAction.RunCommand("/bin/notify", ["--urgent", "two words"], 5)), Reading, "myups");

        var request = Assert.Single(f.Commands.Requests);
        Assert.Equal("/bin/notify", request.FileName);
        Assert.Equal(["--urgent", "two words"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
        Assert.Equal("Test rule", request.Environment["COCONUT_RULE"]);
        Assert.Equal("myups", request.Environment["COCONUT_UPS"]);
        Assert.Equal("12.5", request.Environment["COCONUT_BATTERY_CHARGE"]);
        Assert.Equal("300", request.Environment["COCONUT_BATTERY_RUNTIME"]);
        Assert.Equal(string.Empty, request.Environment["COCONUT_LOAD"]);
    }

    [Fact]
    public async Task RunCommand_NonZeroExit_LogsTheExitCodeAndATruncatedStandardError()
    {
        using var f = new Fixture();
        f.Commands.Behavior = (_, _) => Task.FromResult(new CommandResult(3, false, new string('x', 2000)));

        await f.Runner.RunAsync(Match(RuleAction.RunCommand("tool")), Reading, "myups");

        var entry = Assert.Single(f.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("exited with code 3", entry.Message);
        Assert.Contains(new string('x', RuleActionRunner.MaxLoggedStandardErrorLength) + "...", entry.Message);
        Assert.DoesNotContain(new string('x', RuleActionRunner.MaxLoggedStandardErrorLength + 1), entry.Message);
    }

    [Fact]
    public async Task RunCommand_Timeout_IsLoggedAsAWarning()
    {
        using var f = new Fixture();
        f.Commands.Behavior = (_, _) => Task.FromResult(new CommandResult(null, true, string.Empty));

        await f.Runner.RunAsync(Match(RuleAction.RunCommand("tool", timeoutSeconds: 7)), Reading, "myups");

        var entry = Assert.Single(f.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("did not finish within 7s and was killed", entry.Message);
    }

    [Fact]
    public async Task Webhook_PostsTheJsonPayload()
    {
        using var f = new Fixture();

        await f.Runner.RunAsync(Match(RuleAction.Webhook("https://hooks.example/path?token=secret")), Reading, "myups");

        var (uri, body) = Assert.Single(f.Handler.Requests);
        Assert.Equal("https://hooks.example/path?token=secret", uri?.ToString());
        using var document = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal("Test rule", document.RootElement.GetProperty("rule").GetString());
        Assert.Equal("myups", document.RootElement.GetProperty("ups").GetString());
        Assert.Equal(12.5, document.RootElement.GetProperty("batteryCharge").GetDouble());
        Assert.Equal(f.Time.GetUtcNow(), document.RootElement.GetProperty("timestamp").GetDateTimeOffset());

        // Only scheme and host are logged: the path or query of a webhook URL usually carries a secret token.
        Assert.DoesNotContain(f.Logger.Entries, e => e.Message.Contains("secret"));
    }

    [Fact]
    public async Task Webhook_ErrorStatus_IsLoggedAsAWarning()
    {
        using var f = new Fixture();
        f.Handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await f.Runner.RunAsync(Match(RuleAction.Webhook("https://hooks.example/x")), Reading, "myups");

        Assert.Contains(f.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("HTTP 500"));
    }

    [Fact]
    public async Task Webhook_Timeout_IsLoggedAndEndsTheAction()
    {
        using var f = new Fixture();
        f.Runner.WebhookTimeout = TimeSpan.FromMilliseconds(50);
        f.Handler.Respond = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        await f.Runner.RunAsync(Match(RuleAction.Webhook("https://hooks.example/x")), Reading, "myups");

        Assert.Contains(f.Logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("timed out"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://example.org/x")]
    public async Task Webhook_WithoutAValidHttpUrl_SendsNothing(string url)
    {
        using var f = new Fixture();

        await f.Runner.RunAsync(Match(RuleAction.Webhook(url)), Reading, "myups");

        Assert.Empty(f.Handler.Requests);
        Assert.Contains(f.Logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task FailingActions_AreIsolated_AndTheOthersStillRun()
    {
        using var f = new Fixture();
        f.Notifications.Throw = new InvalidOperationException("no desktop");
        f.Commands.Behavior = (_, _) => throw new System.ComponentModel.Win32Exception("file not found");
        f.Handler.Respond = _ => throw new HttpRequestException("unreachable");

        await f.Runner.RunAsync(
            Match(
                RuleAction.Notify("a"),
                RuleAction.RunCommand("missing"),
                RuleAction.Webhook("https://hooks.example/x"),
                RuleAction.Log("still logged")),
            Reading,
            "myups");

        Assert.Equal(3, f.Logger.Entries.Count(e => e.Level == LogLevel.Error && e.Exception is not null));
        Assert.Contains(f.Logger.Entries, e => e.Message == "Rule 'Test rule': still logged");
    }

    [Fact]
    public async Task StopComputerActions_AreIgnored()
    {
        using var f = new Fixture();

        await f.Runner.RunAsync(
            Match(RuleAction.StopComputer(StopAction.Shutdown), RuleAction.Notify("only this")), Reading, "myups");

        Assert.Equal("only this", Assert.Single(f.Notifications.Shown).Message);
        Assert.Empty(f.Commands.Requests);
    }

    [Fact]
    public async Task Run_IsFireAndForget_ANeverEndingCommandDoesNotBlockTheCaller_AndDisposeCancelsIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture();
        f.Commands.Behavior = async (_, token) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }

            return new CommandResult(0, false, string.Empty);
        };

        f.Runner.Run(Match(RuleAction.RunCommand("sleeper")), Reading, "myups"); // returns at once.
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        f.Runner.Dispose();

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
