using Avalonia;
using CocoNut.App.Services;

namespace CocoNut.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Checked before Avalonia starts: shutting the application lifetime down from inside
        // OnFrameworkInitializationCompleted crashes because the dispatcher loop has not started yet.
        using var singleInstanceGuard = new SingleInstanceGuard();
        if (!singleInstanceGuard.IsFirstInstance)
        {
            Console.Error.WriteLine("Coco.Nut is already running for this user; asking it to show its window.");
            var delivered = SingleInstanceIpc
                .TryRequestShowAsync(SingleInstanceIpc.GetPipeName(), TimeSpan.FromSeconds(2))
                .GetAwaiter().GetResult();
            if (!delivered)
            {
                Console.Error.WriteLine("Could not reach the running instance.");
            }

            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
