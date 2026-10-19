using System.Globalization;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Updates;
using CocoNut.Localization;

namespace CocoNut.App.Tests.ViewModels;

public sealed class UpdateAvailableViewModelTests
{
    public UpdateAvailableViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Message_formats_the_new_and_current_version()
    {
        var result = UpdateCheckResult.FromRelease(true, ReleaseVersion.TryParse("2.0.0")!, new CocoNut.Core.Updates.GitHubRelease
        {
            TagName = "v2.0.0",
            Name = "Coco.Nut 2.0",
            HtmlUrl = "https://example.invalid/releases/v2.0.0",
            Body = "Release notes body.",
            PublishedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
        });

        var vm = new UpdateAvailableViewModel(result, ReleaseVersion.TryParse("1.5.0")!, new FakeShellLauncher(), new FakeAppUpdater(), new FakeShutdownEvents(), new ImmediateUiDispatcher());

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Update_Message, ReleaseVersion.TryParse("2.0.0")!, ReleaseVersion.TryParse("1.5.0")!);
        Assert.Equal(expected, vm.MessageText);
        Assert.Equal("Coco.Nut 2.0", vm.ReleaseName);
        Assert.True(vm.HasReleaseName);
        Assert.True(vm.HasPublishedDate);
        Assert.Equal("Release notes body.", vm.ReleaseNotes);
        Assert.True(vm.CanOpenReleasePage);
    }

    [Fact]
    public void Missing_release_name_and_date_hide_their_rows()
    {
        var result = new UpdateCheckResult { LatestVersion = ReleaseVersion.TryParse("2.0.0")!, IsUpdateAvailable = true };

        var vm = new UpdateAvailableViewModel(result, ReleaseVersion.TryParse("1.0.0")!, new FakeShellLauncher(), new FakeAppUpdater(), new FakeShutdownEvents(), new ImmediateUiDispatcher());

        Assert.False(vm.HasReleaseName);
        Assert.False(vm.HasPublishedDate);
        Assert.False(vm.CanOpenReleasePage);
    }

    [Fact]
    public void Open_release_page_launches_the_release_url()
    {
        var result = UpdateCheckResult.FromRelease(true, ReleaseVersion.TryParse("2.0.0")!, new CocoNut.Core.Updates.GitHubRelease
        {
            TagName = "v2.0.0",
            HtmlUrl = "https://example.invalid/releases/v2.0.0",
        });
        var shellLauncher = new FakeShellLauncher();
        var vm = new UpdateAvailableViewModel(result, ReleaseVersion.TryParse("1.0.0")!, shellLauncher, new FakeAppUpdater(), new FakeShutdownEvents(), new ImmediateUiDispatcher());

        vm.OpenReleasePageCommand.Execute(null);

        Assert.Contains("https://example.invalid/releases/v2.0.0", shellLauncher.OpenedUrls);
    }

    [Fact]
    public void Open_release_page_is_disabled_without_a_url()
    {
        var result = new UpdateCheckResult { LatestVersion = ReleaseVersion.TryParse("2.0.0")!, IsUpdateAvailable = true };
        var vm = new UpdateAvailableViewModel(result, ReleaseVersion.TryParse("1.0.0")!, new FakeShellLauncher(), new FakeAppUpdater(), new FakeShutdownEvents(), new ImmediateUiDispatcher());

        Assert.False(vm.OpenReleasePageCommand.CanExecute(null));
    }

    private static UpdateAvailableViewModel CreateViewModel(FakeAppUpdater updater, FakeShutdownEvents? shutdownEvents = null)
    {
        var result = UpdateCheckResult.FromRelease(true, ReleaseVersion.TryParse("2.0.0")!, new CocoNut.Core.Updates.GitHubRelease
        {
            TagName = "v2.0.0",
            HtmlUrl = "https://example.invalid/releases/v2.0.0",
        });
        return new UpdateAvailableViewModel(
            result, ReleaseVersion.TryParse("1.0.0")!, new FakeShellLauncher(), updater, shutdownEvents ?? new FakeShutdownEvents(), new ImmediateUiDispatcher());
    }

    [Fact]
    public void Install_button_is_hidden_and_release_page_is_primary_when_not_installed()
    {
        var vm = CreateViewModel(new FakeAppUpdater { CanSelfUpdate = false });

        Assert.False(vm.CanSelfUpdate);
        Assert.True(vm.IsReleasePagePrimary);
        Assert.False(vm.DownloadAndInstallCommand.CanExecute(null));
    }

    [Fact]
    public void Install_button_is_offered_and_release_page_is_secondary_when_installed()
    {
        var vm = CreateViewModel(new FakeAppUpdater { CanSelfUpdate = true });

        Assert.True(vm.CanSelfUpdate);
        Assert.False(vm.IsReleasePagePrimary);
        Assert.True(vm.DownloadAndInstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Install_reports_progress_and_disables_itself_while_running()
    {
        var gate = new TaskCompletionSource();
        var updater = new FakeAppUpdater { CanSelfUpdate = true, ProgressSteps = [10, 55], Gate = gate.Task };
        var vm = CreateViewModel(updater);

        var running = vm.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.True(vm.IsInstalling);
        Assert.Equal(55, vm.Progress);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Update_Downloading, 55), vm.ProgressText);
        Assert.False(vm.DownloadAndInstallCommand.CanExecute(null));

        gate.SetResult();
        await running;

        Assert.False(vm.IsInstalling);
        Assert.True(vm.DownloadAndInstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Install_failure_shows_the_error_and_keeps_the_release_page_button()
    {
        var updater = new FakeAppUpdater { CanSelfUpdate = true, Error = "disk full" };
        var vm = CreateViewModel(updater);

        await vm.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.True(vm.HasError);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Update_InstallFailed, "disk full"), vm.ErrorText);
        Assert.True(vm.OpenReleasePageCommand.CanExecute(null));
        Assert.True(vm.DownloadAndInstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task Install_is_refused_while_a_shutdown_is_pending()
    {
        var updater = new FakeAppUpdater { CanSelfUpdate = true };
        var vm = CreateViewModel(updater, new FakeShutdownEvents { IsShutdownPending = true });

        await vm.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.Equal(0, updater.Calls);
        Assert.Equal(Strings.Update_BlockedByShutdown, vm.ErrorText);
        Assert.False(vm.IsInstalling);
    }
}
