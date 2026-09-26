using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using CocoNut.Core;
using CocoNut.Core.Logging;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.Services;

/// <summary>
/// Installs global exception handlers and, for a fatal one, writes a crash report text file to
/// <see cref="AppPaths.DataDirectory"/> - version, OS, the exception, and the last 50 buffered log entries -
/// ports WinNUT's <c>ApplicationEvents.vb</c> <c>GenerateCrashReport</c>. No dialog is shown; the report is for a
/// later bug report.
/// </summary>
public sealed class CrashReporter
{
    private readonly LogBuffer _logBuffer;
    private readonly ILogger<CrashReporter> _logger;
    private readonly string _dataDirectory;
    private bool _installed;

    /// <param name="logBuffer">Supplies the last log entries included in a crash report.</param>
    /// <param name="logger">Receives a record of every handled exception, fatal or not.</param>
    /// <param name="dataDirectory">Directory the report file is written to; defaults to <see cref="AppPaths.DataDirectory"/>.</param>
    public CrashReporter(LogBuffer logBuffer, ILogger<CrashReporter> logger, string? dataDirectory = null)
    {
        _logBuffer = logBuffer ?? throw new ArgumentNullException(nameof(logBuffer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;
    }

    /// <summary>
    /// Subscribes to <see cref="AppDomain.UnhandledException"/>, <see cref="TaskScheduler.UnobservedTaskException"/>
    /// and Avalonia's <see cref="Dispatcher.UIThread"/>.<c>UnhandledException</c>. Safe to call more than once;
    /// only the first call subscribes.
    /// </summary>
    public void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        _logger.LogCritical(exception, "Unhandled exception; the process is terminating.");
        WriteCrashReport(exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // A background Task's exception that nobody awaited/observed. Not fatal in modern .NET (the process keeps
        // running either way) - logged for diagnostics only, and marked observed so it does not surface again.
        _logger.LogError(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Left unhandled, this would tear down the UI thread (and with it the whole app) over what is often a
        // single bad render/binding - logging plus a crash report and keeping the app alive protects the machine
        // better than crashing it, in the spirit of the rest of this app (see ShutdownCoordinator's remarks).
        _logger.LogError(e.Exception, "Unhandled exception on the UI thread.");
        WriteCrashReport(e.Exception);
        e.Handled = true;
    }

    /// <summary>Writes the crash report file. Never throws; a failure to write is logged instead.</summary>
    public void WriteCrashReport(Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var path = Path.Combine(_dataDirectory, $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, BuildReport(exception));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to write the crash report.");
        }
    }

    private string BuildReport(Exception? exception)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? typeof(CrashReporter).Assembly.GetName().Version;

        var culture = CultureInfo.InvariantCulture;
        var report = new StringBuilder()
            .AppendLine("Coco.Nut crash report")
            .AppendLine(culture, $"Time: {DateTimeOffset.Now:O}")
            .AppendLine(culture, $"Version: {version}")
            .AppendLine(culture, $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")
            .AppendLine(culture, $".NET: {RuntimeInformation.FrameworkDescription}")
            .AppendLine()
            .AppendLine("Exception:")
            .AppendLine(exception?.ToString() ?? "(none captured)")
            .AppendLine()
            .AppendLine("Last log entries:");

        foreach (var entry in _logBuffer.Snapshot().TakeLast(50))
        {
            report.AppendLine(culture, $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Category}: {entry.Message}");
        }

        return report.ToString();
    }
}
