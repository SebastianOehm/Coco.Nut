using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the update-available window (WinNUT's <c>Forms/UpdateAvailableForm.vb</c>): the new/current
/// version headline, release name/date, plain-text release notes, and the buttons to get the update. An installed
/// build (<see cref="IAppUpdater.CanSelfUpdate"/>) offers "Download and install", which shows progress and
/// restarts into the new version; if that fails the error is shown and "Open release page" remains as the
/// fallback. A portable or dev build only offers "Open release page". The in-app install is refused while a
/// shutdown countdown is active, since the restart would drop it.
/// </summary>
public sealed partial class UpdateAvailableViewModel : ObservableObject
{
    private readonly IShellLauncher _shellLauncher;
    private readonly IAppUpdater _appUpdater;
    private readonly IShutdownEvents _shutdownEvents;
    private readonly IUiDispatcher _dispatcher;
    private readonly string? _releaseUrl;

    public UpdateAvailableViewModel(
        UpdateCheckResult result,
        ReleaseVersion currentVersion,
        IShellLauncher shellLauncher,
        IAppUpdater appUpdater,
        IShutdownEvents shutdownEvents,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(currentVersion);
        _shellLauncher = shellLauncher ?? throw new ArgumentNullException(nameof(shellLauncher));
        _appUpdater = appUpdater ?? throw new ArgumentNullException(nameof(appUpdater));
        _shutdownEvents = shutdownEvents ?? throw new ArgumentNullException(nameof(shutdownEvents));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        MessageText = string.Format(CultureInfo.CurrentCulture, Strings.Update_Message, result.LatestVersion, currentVersion);
        ReleaseName = result.ReleaseName ?? string.Empty;
        HasReleaseName = !string.IsNullOrWhiteSpace(ReleaseName);
        PublishedDateText = result.PublishedAt is { } published ? published.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty;
        HasPublishedDate = result.PublishedAt is not null;
        ReleaseNotes = result.ReleaseNotes ?? string.Empty;
        _releaseUrl = result.ReleaseUrl;
        CanOpenReleasePage = !string.IsNullOrWhiteSpace(_releaseUrl);
        CanSelfUpdate = _appUpdater.CanSelfUpdate;
    }

    public string MessageText { get; }

    public string ReleaseName { get; }

    public bool HasReleaseName { get; }

    public string PublishedDateText { get; }

    public bool HasPublishedDate { get; }

    public string ReleaseNotes { get; }

    public bool CanOpenReleasePage { get; }

    /// <summary>Whether the "Download and install" button is offered (installed builds only).</summary>
    public bool CanSelfUpdate { get; }

    /// <summary>Whether "Open release page" is the primary (accent) button, i.e. there is no in-app install.</summary>
    public bool IsReleasePagePrimary => !CanSelfUpdate;

    /// <summary>Download progress, 0-100.</summary>
    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private bool _isInstalling;

    /// <summary>The last failure, shown below the release notes; empty when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorText = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    public string ProgressText => string.Format(CultureInfo.CurrentCulture, Strings.Update_Downloading, Progress);

    partial void OnProgressChanged(int value) => OnPropertyChanged(nameof(ProgressText));

    [RelayCommand(CanExecute = nameof(CanOpenReleasePage))]
    private void OpenReleasePage()
    {
        if (_releaseUrl is not null)
        {
            _shellLauncher.OpenUrl(_releaseUrl);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task DownloadAndInstallAsync()
    {
        ErrorText = string.Empty;
        if (_shutdownEvents.IsShutdownPending)
        {
            ErrorText = Strings.Update_BlockedByShutdown;
            return;
        }

        Progress = 0;
        IsInstalling = true;
        try
        {
            var outcome = await _appUpdater.DownloadAndInstallAsync(new DispatcherProgress(this)).ConfigureAwait(true);
            ErrorText = string.Format(CultureInfo.CurrentCulture, Strings.Update_InstallFailed, outcome.Error);
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>Receives progress from the updater's download thread and applies it on the UI thread.</summary>
    private sealed class DispatcherProgress(UpdateAvailableViewModel owner) : IProgress<int>
    {
        public void Report(int value) => owner._dispatcher.Post(() => owner.Progress = value);
    }

    private bool CanInstall() => CanSelfUpdate && !IsInstalling;

    partial void OnIsInstallingChanged(bool value) => DownloadAndInstallCommand.NotifyCanExecuteChanged();
}
