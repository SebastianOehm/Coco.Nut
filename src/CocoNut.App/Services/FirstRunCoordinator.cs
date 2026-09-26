using CocoNut.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.Services;

/// <summary>
/// Orchestrates first-run setup (<see cref="GeneralSettings.IsFirstRun"/>): instead of auto-connecting to the
/// default host, the app shows the Settings window first; once the user has saved it at least once,
/// <see cref="RunAsync"/> clears the first-run flag and connects with whatever connection settings resulted from
/// that save. Takes the actual window-showing as a delegate so this orchestration is unit testable without an
/// Avalonia window (see <c>App.axaml.cs</c> for the real delegate, which shows a real <c>SettingsWindow</c> and
/// waits for it to close).
/// </summary>
public sealed class FirstRunCoordinator
{
    private readonly ISettingsService _settingsService;
    private readonly IUpsMonitorEvents _monitor;
    private readonly Func<Task<bool>> _showSettingsAndReportWhetherSaved;
    private readonly ILogger<FirstRunCoordinator> _logger;

    /// <param name="showSettingsAndReportWhetherSaved">
    /// Shows the Settings window, waits for it to close, and returns whether at least one Save/Apply happened
    /// while it was open.
    /// </param>
    public FirstRunCoordinator(
        ISettingsService settingsService,
        IUpsMonitorEvents monitor,
        Func<Task<bool>> showSettingsAndReportWhetherSaved,
        ILogger<FirstRunCoordinator> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _showSettingsAndReportWhetherSaved = showSettingsAndReportWhetherSaved ?? throw new ArgumentNullException(nameof(showSettingsAndReportWhetherSaved));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether the app is currently in its first run (<c>Settings.General.IsFirstRun</c>).</summary>
    public bool IsFirstRun => _settingsService.Current.General.IsFirstRun;

    /// <summary>
    /// Runs the first-run flow: shows Settings and, once it has been saved at least once, clears
    /// <see cref="GeneralSettings.IsFirstRun"/> and connects. Does nothing if <see cref="IsFirstRun"/> is
    /// already <see langword="false"/>. If the user closes the window without ever saving, the app remains in
    /// first-run state and this flow runs again on the next start.
    /// </summary>
    public async Task RunAsync()
    {
        if (!IsFirstRun)
        {
            return;
        }

        var saved = await _showSettingsAndReportWhetherSaved().ConfigureAwait(true);
        if (!saved)
        {
            _logger.LogInformation("First-run setup was closed without saving; it will run again on the next start.");
            return;
        }

        var updated = _settingsService.Current.Clone();
        updated.General.IsFirstRun = false;
        _settingsService.Save(updated);

        var current = _settingsService.Current;
        if (string.IsNullOrWhiteSpace(current.Connection.Host))
        {
            return;
        }

        try
        {
            await _monitor.StartAsync(current.Connection, current.Calibration.InputFrequencyNominal).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start the UPS monitor after first-run setup.");
        }
    }
}
