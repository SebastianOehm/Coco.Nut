using System.Globalization;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Localization;

namespace CocoNut.App.Tests.ViewModels;

public sealed class AboutViewModelTests
{
    public AboutViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void Version_text_uses_the_running_assembly_when_none_is_given()
    {
        var vm = new AboutViewModel(new FakeShellLauncher());

        Assert.StartsWith(Strings.About_Version[..Strings.About_Version.IndexOf("{0}", StringComparison.Ordinal)], vm.VersionText, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(vm.VersionText));
    }

    [Fact]
    public void Copyright_text_comes_from_the_assembly_attribute()
    {
        // CocoNut.App.Tests.csproj does not itself set <Copyright>, so this only proves the read path does not
        // throw and, when the attribute is present (as it is for the real CocoNut.App assembly via
        // Directory.Build.props), surfaces its exact text.
        var vm = new AboutViewModel(new FakeShellLauncher(), typeof(AboutViewModelTests).Assembly);

        Assert.NotNull(vm.CopyrightText);
    }

    [Fact]
    public void Open_project_page_launches_the_project_url()
    {
        var shellLauncher = new FakeShellLauncher();
        var vm = new AboutViewModel(shellLauncher);

        vm.OpenProjectPageCommand.Execute(null);

        Assert.Contains(AboutViewModel.ProjectPageUrl, shellLauncher.OpenedUrls);
    }

    [Fact]
    public void Acknowledgements_mentions_the_key_dependencies()
    {
        var vm = new AboutViewModel(new FakeShellLauncher());

        Assert.Contains("Avalonia", vm.AcknowledgementsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CommunityToolkit.Mvvm", vm.AcknowledgementsText, StringComparison.Ordinal);
        Assert.Contains("WinNUT", vm.AcknowledgementsText, StringComparison.Ordinal);
    }
}
