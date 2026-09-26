using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.Services;

/// <summary>
/// Cross-platform single-instance hand-off: instead of a second launch simply exiting (see
/// <see cref="SingleInstanceGuard"/>), it tells the already-running first instance to show its main window, then
/// exits itself. Built on <see cref="NamedPipeServerStream"/>/<see cref="NamedPipeClientStream"/>, which .NET backs
/// with Unix domain sockets on Linux/macOS, so the same code works on every desktop platform Coco.Nut targets.
/// </summary>
public static class SingleInstanceIpc
{
    internal const string ShowMessage = "show";

    /// <summary>
    /// The per-user pipe name the first instance listens on and a second instance connects to. Scoped to the
    /// current user (like <see cref="SingleInstanceGuard"/>'s mutex name) so different user sessions on the same
    /// machine never see each other's instance.
    /// </summary>
    public static string GetPipeName(string appName = "CocoNut") => $"{appName}_ShowRequest_{Environment.UserName}";

    /// <summary>
    /// Connects to <paramref name="pipeName"/> and asks the listening instance to show its window. Returns
    /// <see langword="false"/> (never throws) if nothing is listening or the request could not be delivered
    /// within <paramref name="timeout"/> - the caller should fall back to just exiting either way.
    /// </summary>
    public static async Task<bool> TryRequestShowAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(timeout);
            await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            var payload = Encoding.UTF8.GetBytes(SingleInstanceIpc.ShowMessage);
            await client.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await client.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Listens on <paramref name="pipeName"/> for "show" requests from later launches and invokes
    /// <paramref name="onShowRequested"/> for each one, until disposed. Every failure is logged and swallowed -
    /// this must never take the whole app down - and the server keeps listening for the next request.
    /// </summary>
    public static SingleInstanceServer StartServer(string pipeName, Action onShowRequested, ILogger logger) =>
        new(pipeName, onShowRequested, logger);
}

/// <summary>The listening half of <see cref="SingleInstanceIpc"/>; see <see cref="SingleInstanceIpc.StartServer"/>.</summary>
public sealed class SingleInstanceServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Action _onShowRequested;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoopTask;

    internal SingleInstanceServer(string pipeName, Action onShowRequested, ILogger logger)
    {
        _pipeName = pipeName;
        _onShowRequested = onShowRequested ?? throw new ArgumentNullException(nameof(onShowRequested));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        var server = CreateServer();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug(ex, "Single-instance IPC server error while waiting for a connection; retrying.");
                    server.Dispose();
                    server = CreateServer();
                    continue;
                }

                // A client just connected to `connected`. Immediately bind a fresh instance to take its place as
                // the listener before reading/processing this one (which can take a moment), so the pipe is never
                // left without something listening for the next launch's connection attempt.
                var connected = server;
                server = CreateServer();

                try
                {
                    using var buffer = new MemoryStream();
                    await connected.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                    var message = Encoding.UTF8.GetString(buffer.ToArray());

                    if (string.Equals(message, SingleInstanceIpc.ShowMessage, StringComparison.Ordinal))
                    {
                        _onShowRequested();
                    }
                    else if (message.Length > 0)
                    {
                        _logger.LogDebug("Ignoring an unrecognized single-instance IPC message: {Message}", message);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best effort: log and keep listening for the next connection attempt.
                    _logger.LogDebug(ex, "Single-instance IPC server error while reading a connection.");
                }
                finally
                {
                    connected.Dispose();
                }
            }
        }
        finally
        {
            server.Dispose();
        }
    }

    /// <summary>
    /// A fresh server-side pipe instance for <see cref="_pipeName"/>. Multiple instances may exist concurrently
    /// (see <see cref="AcceptLoopAsync"/>), which is exactly what <see cref="NamedPipeServerStream"/>'s
    /// multi-instance support is for.
    /// </summary>
    private NamedPipeServerStream CreateServer() => new(
        _pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _acceptLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop observes the cancellation and exits.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Single-instance IPC server's accept loop ended with an error.");
        }

        _cts.Dispose();
    }
}
