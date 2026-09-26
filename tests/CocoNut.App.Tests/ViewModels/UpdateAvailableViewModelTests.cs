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
        var result = UpdateCheckResult.FromRelease(true, new Version(2, 0, 0), new CocoNut.Core.Updates.GitHubRelease
        {
            TagName = "v2.0.0",
            Name = "Coco.Nut 2.0",
            HtmlUrl = "https://example.invalid/releases/v2.0.0",
            Body = "Release notes body.",
            PublishedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero),
        });

        var vm = new UpdateAvailableViewModel(result, new Version(1, 5, 0), new FakeShellLauncher());

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Update_Message, new Version(2, 0, 0), new Version(1, 5, 0));
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
        var result = new UpdateCheckResult { LatestVersion = new Version(2, 0, 0), IsUpdateAvailable = true };

        var vm = new UpdateAvailableViewModel(result, new Version(1, 0, 0), new FakeShellLauncher());

        Assert.False(vm.HasReleaseName);
        Assert.False(vm.HasPublishedDate);
        Assert.False(vm.CanOpenReleasePage);
    }

    [Fact]
    public void Open_release_page_launches_the_release_url()
    {
        var result = UpdateCheckResult.FromRelease(true, new Version(2, 0, 0), new CocoNut.Core.Updates.GitHubRelease
        {
            TagName = "v2.0.0",
            HtmlUrl = "https://example.invalid/releases/v2.0.0",
        });
        var shellLauncher = new FakeShellLauncher();
        var vm = new UpdateAvailableViewModel(result, new Version(1, 0, 0), shellLauncher);

        vm.OpenReleasePageCommand.Execute(null);

        Assert.Contains("https://example.invalid/releases/v2.0.0", shellLauncher.OpenedUrls);
    }

    [Fact]
    public void Open_release_page_is_disabled_without_a_url()
    {
        var result = new UpdateCheckResult { LatestVersion = new Version(2, 0, 0), IsUpdateAvailable = true };
        var vm = new UpdateAvailableViewModel(result, new Version(1, 0, 0), new FakeShellLauncher());

        Assert.False(vm.OpenReleasePageCommand.CanExecute(null));
    }
}
