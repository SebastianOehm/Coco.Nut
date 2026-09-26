using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CocoNut.App.Views;

namespace CocoNut.App.Tests;

/// <summary>
/// Not a correctness test: renders the WP-F demo <see cref="MainWindow"/> (see MainWindow.axaml) headlessly
/// and saves a PNG for the architect's review to the repository's gitignored <c>artifacts/</c> folder.
/// Safe to keep in the suite - it only writes outside the repository's tracked files.
/// </summary>
public class DemoScreenshotTests
{
    [AvaloniaFact]
    public void Demo_window_screenshot_is_saved_to_artifacts_gauges_png()
    {
        var window = new MainWindow();
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        string artifactsDirectory = Path.Combine(FindRepositoryRoot(), "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        frame.Save(Path.Combine(artifactsDirectory, "gauges.png"));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CocoNut.sln")))
            {
                return directory.FullName;
            }
        }

        return AppContext.BaseDirectory;
    }
}
