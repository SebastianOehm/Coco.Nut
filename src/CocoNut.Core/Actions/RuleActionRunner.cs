using System.Globalization;
using System.Net.Http.Json;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Actions;

/// <summary>
/// Default <see cref="IRuleActionRunner"/>. Every action runs on the thread pool, independent of the others; whatever
/// goes wrong (an exception, a timeout, a non-success HTTP status, a non-zero exit code) is logged and stops there.
/// Notifications go through <see cref="INotificationService"/>, which is responsible for any thread marshalling.
/// Secrets stay out of the log: only the scheme and host of a webhook URL and the file name of a command are written.
/// </summary>
public sealed class RuleActionRunner : IRuleActionRunner, IDisposable
{
    /// <summary>The time a webhook gets to answer (10 s; settable for tests).</summary>
    internal TimeSpan WebhookTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How many characters of a command's standard error are logged.</summary>
    internal const int MaxLoggedStandardErrorLength = 500;

    private readonly INotificationService _notifications;
    private readonly ICommandRunner _commandRunner;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _disposeCts = new();

    public RuleActionRunner(
        INotificationService notifications,
        ICommandRunner commandRunner,
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<RuleActionRunner> logger)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public void Run(RuleMatch match, UpsReading? reading, string upsName) => _ = RunAsync(match, reading, upsName);

    /// <summary>
    /// Like <see cref="Run"/>, but the returned task completes when every action has ended. Never faults.
    /// </summary>
    public Task RunAsync(RuleMatch match, UpsReading? reading, string upsName)
    {
        ArgumentNullException.ThrowIfNull(match);

        try
        {
            var actions = match.Rule.Actions.Where(a => a is { Kind: not ActionKind.StopComputer }).ToList();
            if (actions.Count == 0)
            {
                return Task.CompletedTask;
            }

            var context = new ActionContext(match.Rule, upsName, reading, _timeProvider.GetUtcNow());
            return Task.WhenAll(actions.Select(action => Task.Run(() => ExecuteAsync(action, context))));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start the actions of rule '{Rule}'.", match.Rule.Name);
            return Task.CompletedTask;
        }
    }

    private async Task ExecuteAsync(RuleAction action, ActionContext context)
    {
        try
        {
            switch (action.Kind)
            {
                case ActionKind.Notify:
                    _notifications.Notify(
                        context.Rule.Name,
                        MessageTemplate.Expand(action.Message, context, CultureInfo.CurrentCulture),
                        NotificationKind.Info);
                    break;

                case ActionKind.Log:
                    _logger.Log(
                        action.LogLevel,
                        "Rule '{Rule}': {Message}",
                        context.Rule.Name,
                        MessageTemplate.Expand(action.Message, context, CultureInfo.InvariantCulture));
                    break;

                case ActionKind.RunCommand:
                    await RunCommandAsync(action, context).ConfigureAwait(false);
                    break;

                case ActionKind.Webhook:
                    await SendWebhookAsync(action, context).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            // The application is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Action {Action} of rule '{Rule}' failed.", action.Kind, context.Rule.Name);
        }
    }

    private async Task RunCommandAsync(RuleAction action, ActionContext context)
    {
        var request = new CommandRequest(
            action.Command,
            action.Arguments ?? [],
            context.GetEnvironment(),
            TimeSpan.FromSeconds(Math.Max(1, action.TimeoutSeconds)));

        var result = await _commandRunner.RunAsync(request, _disposeCts.Token).ConfigureAwait(false);
        if (result.TimedOut)
        {
            _logger.LogWarning(
                "Command '{Command}' of rule '{Rule}' did not finish within {Timeout}s and was killed.",
                action.Command, context.Rule.Name, request.Timeout.TotalSeconds);
        }
        else if (result.ExitCode == 0)
        {
            _logger.LogInformation("Command '{Command}' of rule '{Rule}' finished.", action.Command, context.Rule.Name);
        }
        else
        {
            _logger.LogWarning(
                "Command '{Command}' of rule '{Rule}' exited with code {ExitCode}. Standard error: {StandardError}",
                action.Command, context.Rule.Name, result.ExitCode, Truncate(result.StandardError));
        }
    }

    private async Task SendWebhookAsync(RuleAction action, ActionContext context)
    {
        if (!Uri.TryCreate(action.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            _logger.LogWarning("Webhook of rule '{Rule}' has no valid http/https URL; nothing was sent.", context.Rule.Name);
            return;
        }

        // The request timeout is enforced here instead of on the (shared) HttpClient.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        timeout.CancelAfter(WebhookTimeout);

        var target = $"{uri.Scheme}://{uri.Authority}";
        try
        {
            using var content = JsonContent.Create(WebhookPayload.From(context), WebhookJsonContext.Default.WebhookPayload);
            using var response = await _httpClient.PostAsync(uri, content, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Webhook of rule '{Rule}' delivered to {Target}.", context.Rule.Name, target);
            }
            else
            {
                _logger.LogWarning(
                    "Webhook of rule '{Rule}' to {Target} was answered with HTTP {StatusCode}.",
                    context.Rule.Name, target, (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (!_disposeCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Webhook of rule '{Rule}' to {Target} timed out after {Timeout}s.",
                context.Rule.Name, target, WebhookTimeout.TotalSeconds);
        }
    }

    private static string Truncate(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= MaxLoggedStandardErrorLength ? trimmed : $"{trimmed[..MaxLoggedStandardErrorLength]}...";
    }

    /// <summary>Cancels actions that are still running (commands are killed). The token source is not disposed:
    /// an action that outlives this call may still read its token.</summary>
    public void Dispose() => _disposeCts.Cancel();
}
