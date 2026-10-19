using CocoNut.Core.Settings;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using Velopack;
using Velopack.Sources;

namespace CocoNut.App.Services;

/// <summary>
/// Downloads and installs a new Coco.Nut version in place. Only possible when the app was installed by a Velopack
/// installer (Windows Setup.exe, Linux AppImage, macOS pkg); a portable zip or a dev run cannot update itself
/// and falls back to the release page. <see cref="VelopackAppUpdater"/> is the real implementation.
/// </summary>
public interface IAppUpdater
{
    /// <summary><see langword="true"/> only when running from a Velopack install.</summary>
    bool CanSelfUpdate { get; }

    /// <summary>
    /// Looks for a newer package on the user's update channel, downloads it (reporting 0-100 through
    /// <paramref name="progress"/>), then applies it and restarts the app. On success the process exits and this
    /// call never returns; on failure (no package found, network error, ...) it returns the reason instead of
    /// throwing. Only a cancellation requested through <paramref name="cancellationToken"/> propagates.
    /// </summary>
    /// <param name="canApply">
    /// Checked after the download, right before the update is applied (which restarts the app). When it returns
    /// <see langword="false"/> nothing is applied and the result has <see cref="AppUpdateResult.BlockedByShutdown"/>
    /// set; the downloaded package stays cached for a later attempt.
    /// </param>
    Task<AppUpdateResult> DownloadAndInstallAsync(IProgress<int>? progress, Func<bool> canApply, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IAppUpdater.DownloadAndInstallAsync"/> when the app did not restart.</summary>
/// <param name="Error">Human-readable reason the update was not installed.</param>
/// <param name="BlockedByShutdown">The update was downloaded but not applied because a shutdown countdown became active.</param>
public sealed record AppUpdateResult(string Error, bool BlockedByShutdown = false);

/// <summary>Default <see cref="IAppUpdater"/>, backed by Velopack's <see cref="UpdateManager"/> and the repository's GitHub releases.</summary>
public sealed class VelopackAppUpdater : IAppUpdater
{
    private static readonly string RepoUrl = $"https://github.com/{UpdateChecker.DefaultOwner}/{UpdateChecker.DefaultRepo}";

    private readonly Func<UpdateChannel> _channel;
    private readonly Lazy<bool> _canSelfUpdate = new(DetectInstalled);

    /// <param name="channel">Returns the user's current update channel; read per call so a settings change applies immediately.</param>
    public VelopackAppUpdater(Func<UpdateChannel> channel) =>
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    /// <summary>Whether this process runs from a Velopack install. False (not a crash) when Velopack was never initialised, e.g. in tests.</summary>
    private static bool DetectInstalled()
    {
        try
        {
            return new UpdateManager(new GithubSource(RepoUrl, null, false)).IsInstalled;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool CanSelfUpdate => _canSelfUpdate.Value;

    /// <inheritdoc />
    public async Task<AppUpdateResult> DownloadAndInstallAsync(IProgress<int>? progress, Func<bool> canApply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canApply);

        try
        {
            var source = new GithubSource(RepoUrl, null, _channel() == UpdateChannel.PreRelease);
            var manager = new UpdateManager(source);

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return new AppUpdateResult(Strings.Update_NoInstallablePackage);
            }

            await manager.DownloadUpdatesAsync(update, percent => progress?.Report(percent), cancellationToken).ConfigureAwait(false);
            if (!canApply())
            {
                return new AppUpdateResult(Strings.Update_BlockedByShutdown, BlockedByShutdown: true);
            }

            manager.ApplyUpdatesAndRestart(update.TargetFullRelease);

            // Not reached: ApplyUpdatesAndRestart exits the process.
            return new AppUpdateResult(string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AppUpdateResult(ex.Message);
        }
    }
}
