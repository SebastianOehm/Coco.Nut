using System.Diagnostics;

namespace CocoNut.Core.Actions;

/// <summary>
/// <see cref="ICommandRunner"/> backed by a real <see cref="Process"/>: no shell, no window, the arguments through
/// <see cref="ProcessStartInfo.ArgumentList"/> (so nothing needs quoting or can be injected), standard output
/// discarded, standard error captured.
/// </summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    /// <summary>How many characters of standard error are kept.</summary>
    internal const int MaxStandardErrorLength = 4096;

    /// <summary>How long to wait for the pipes to drain after the process has ended or was killed.</summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public async Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in request.Environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Both pipes must be read or a chatty process blocks on a full pipe buffer.
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = ReadLimitedAsync(process.StandardError);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        if (timedOut)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        var error = await DrainAsync(standardError).ConfigureAwait(false);
        await DrainAsync(standardOutput).ConfigureAwait(false);
        return new CommandResult(timedOut ? null : process.ExitCode, timedOut, error);
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone (or not killable): nothing more to do.
        }
    }

    /// <summary>Reads the stream to its end but keeps only the first <see cref="MaxStandardErrorLength"/> characters.</summary>
    private static async Task<string> ReadLimitedAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        var text = new System.Text.StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (text.Length < MaxStandardErrorLength)
            {
                text.Append(buffer, 0, Math.Min(read, MaxStandardErrorLength - text.Length));
            }
        }

        return text.ToString();
    }

    private static async Task<string> DrainAsync(Task<string> reading)
    {
        try
        {
            return await reading.WaitAsync(DrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return string.Empty; // A grandchild that survived the kill still holds the pipe open.
        }
    }
}
