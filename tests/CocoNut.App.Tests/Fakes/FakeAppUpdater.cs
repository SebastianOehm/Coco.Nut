using CocoNut.App.Services;
using CocoNut.Localization;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Scripted <see cref="IAppUpdater"/>: reports the configured progress, then returns the configured outcome.</summary>
public sealed class FakeAppUpdater : IAppUpdater
{
    public bool CanSelfUpdate { get; set; }

    public int[] ProgressSteps { get; set; } = [];

    public string Error { get; set; } = "update failed";

    public Task? Gate { get; set; }

    public int Calls { get; private set; }

    public async Task<AppUpdateResult> DownloadAndInstallAsync(IProgress<int>? progress, Func<bool> canApply, CancellationToken cancellationToken = default)
    {
        Calls++;
        foreach (var step in ProgressSteps)
        {
            progress?.Report(step);
        }

        if (Gate is not null)
        {
            await Gate;
        }

        if (!canApply())
        {
            return new AppUpdateResult(Strings.Update_BlockedByShutdown, BlockedByShutdown: true);
        }

        return new AppUpdateResult(Error);
    }
}
