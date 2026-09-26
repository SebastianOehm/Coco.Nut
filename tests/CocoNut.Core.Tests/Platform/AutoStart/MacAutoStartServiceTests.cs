using CocoNut.Platform.AutoStart;

namespace CocoNut.Core.Tests.Platform.AutoStart;

public class MacAutoStartServiceTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("cocoNut-mac-autostart-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsEnabled_NoPlist_ReturnsFalse()
    {
        var service = CreateService();

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_True_WritesExpectedPlist()
    {
        var service = CreateService();

        service.SetEnabled(true, "/Applications/Coco.Nut.app/Contents/MacOS/CocoNut");

        var content = File.ReadAllText(Path.Combine(_tempDirectory, "org.nutdotnet.coconut.plist"));
        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "<dict>\n" +
            "\t<key>Label</key>\n" +
            "\t<string>org.nutdotnet.coconut</string>\n" +
            "\t<key>ProgramArguments</key>\n" +
            "\t<array>\n" +
            "\t\t<string>/Applications/Coco.Nut.app/Contents/MacOS/CocoNut</string>\n" +
            "\t</array>\n" +
            "\t<key>RunAtLoad</key>\n" +
            "\t<true/>\n" +
            "</dict>\n" +
            "</plist>\n",
            content);
        Assert.True(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_True_CreatesLaunchAgentsDirectoryWhenMissing()
    {
        var missingDirectory = Path.Combine(_tempDirectory, "LaunchAgents");
        var service = new MacAutoStartService(missingDirectory);

        service.SetEnabled(true, "/Applications/CocoNut");

        Assert.True(Directory.Exists(missingDirectory));
        Assert.True(File.Exists(Path.Combine(missingDirectory, "org.nutdotnet.coconut.plist")));
    }

    [Theory]
    [InlineData("/Apps/Coco & Nut/CocoNut", "/Apps/Coco &amp; Nut/CocoNut")]
    [InlineData("""/Apps/say "hi"/CocoNut""", "/Apps/say &quot;hi&quot;/CocoNut")]
    [InlineData("/Apps/<CocoNut>", "/Apps/&lt;CocoNut&gt;")]
    public void SetEnabled_EscapesProgramArgumentAsXml(string executablePath, string expectedEscapedValue)
    {
        var service = CreateService();

        service.SetEnabled(true, executablePath);

        var content = File.ReadAllText(Path.Combine(_tempDirectory, "org.nutdotnet.coconut.plist"));
        Assert.Contains($"<string>{expectedEscapedValue}</string>", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SetEnabled_False_RemovesPlist()
    {
        var service = CreateService();
        service.SetEnabled(true, "/Applications/CocoNut");

        service.SetEnabled(false, "/Applications/CocoNut");

        Assert.False(File.Exists(Path.Combine(_tempDirectory, "org.nutdotnet.coconut.plist")));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_False_WhenAlreadyDisabled_IsIdempotent()
    {
        var service = CreateService();

        service.SetEnabled(false, "/Applications/CocoNut");
        service.SetEnabled(false, "/Applications/CocoNut");

        Assert.False(service.IsEnabled());
    }

    private MacAutoStartService CreateService() => new(_tempDirectory);
}
