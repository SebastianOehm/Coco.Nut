using Avalonia;
using Avalonia.Headless;
using CocoNut.App;

[assembly: AvaloniaTestApplication(typeof(CocoNut.App.Tests.TestAppBuilder))]

namespace CocoNut.App.Tests;

/// <summary>
/// Configures the Avalonia application used by every <c>[AvaloniaFact]</c>/<c>[AvaloniaTheory]</c> test in
/// this assembly (see <see cref="Avalonia.Headless.HeadlessUnitTestSession"/>). Uses <c>UseSkia()</c> with
/// <c>UseHeadlessDrawing = false</c> so tests get real (software) Skia rendering instead of a no-op
/// renderer - required for <see cref="Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame"/> to
/// return an actual frame.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont();
}
