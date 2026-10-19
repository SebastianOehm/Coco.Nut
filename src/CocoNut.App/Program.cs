using Avalonia;
using CocoNut.App.Services;
using Velopack;

namespace CocoNut.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must be the very first statement: Velopack runs the app with special arguments for its install,
        // update and uninstall hooks, handles them and exits before any of the code below (single-instance
        // guard, UI) may run. In a normal launch this returns immediately.
        VelopackApp.Build().Run();

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
