using CocoNut.Platform.AutoStart;

namespace CocoNut.Core.Tests.Platform.AutoStart;

public class LinuxAutoStartServiceTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("cocoNut-linux-autostart-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void IsEnabled_NoDesktopFile_ReturnsFalse()
    {
        var service = CreateService();

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_True_WritesExpectedDesktopEntry()
    {
        var service = CreateService();

        service.SetEnabled(true, "/usr/bin/coconut");

        var content = File.ReadAllText(Path.Combine(_tempDirectory, "coconut.desktop"));
        Assert.Equal(
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Coco.Nut\n" +
            "Exec=\"/usr/bin/coconut\"\n" +
            "X-GNOME-Autostart-enabled=true\n" +
            "Terminal=false\n",
            content);
        Assert.True(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_True_CreatesAutostartDirectoryWhenMissing()
    {
        var missingDirectory = Path.Combine(_tempDirectory, "autostart");
        var service = new LinuxAutoStartService(missingDirectory);

        service.SetEnabled(true, "/usr/bin/coconut");

        Assert.True(Directory.Exists(missingDirectory));
        Assert.True(File.Exists(Path.Combine(missingDirectory, "coconut.desktop")));
    }

    [Theory]
    [InlineData("/opt/Coco Nut/CocoNut", "/opt/Coco Nut/CocoNut")]
    [InlineData("""/opt/say "hi"/CocoNut""", """/opt/say \"hi\"/CocoNut""")]
    [InlineData("/opt/$HOME/CocoNut", """/opt/\$HOME/CocoNut""")]
    [InlineData("""/opt/`whoami`/CocoNut""", """/opt/\`whoami\`/CocoNut""")]
    [InlineData("""C:\path\CocoNut""", """C:\\path\\CocoNut""")]
    [InlineData("/opt/100%/CocoNut", "/opt/100%%/CocoNut")]
    public void SetEnabled_EscapesExecValuePerDesktopEntrySpec(string executablePath, string expectedEscapedValue)
    {
        var service = CreateService();

        service.SetEnabled(true, executablePath);

        var content = File.ReadAllText(Path.Combine(_tempDirectory, "coconut.desktop"));
        Assert.Contains($"Exec=\"{expectedEscapedValue}\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SetEnabled_False_RemovesDesktopFile()
    {
        var service = CreateService();
        service.SetEnabled(true, "/usr/bin/coconut");

        service.SetEnabled(false, "/usr/bin/coconut");

        Assert.False(File.Exists(Path.Combine(_tempDirectory, "coconut.desktop")));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_False_WhenAlreadyDisabled_IsIdempotent()
    {
        var service = CreateService();

        service.SetEnabled(false, "/usr/bin/coconut");
        service.SetEnabled(false, "/usr/bin/coconut");

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_True_Twice_IsIdempotent()
    {
        var service = CreateService();

        service.SetEnabled(true, "/usr/bin/coconut");
        service.SetEnabled(true, "/usr/bin/coconut");

        Assert.True(service.IsEnabled());
        Assert.Single(Directory.GetFiles(_tempDirectory));
    }

    private LinuxAutoStartService CreateService() => new(_tempDirectory);
}
