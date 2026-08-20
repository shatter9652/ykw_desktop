using Avalonia;
using System;
using System.Diagnostics;
using System.IO;

namespace YKWHome.App;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Redirect System.Diagnostics.Trace to Console so Avalonia logs appear
        // in the terminal for both Debug and Release builds.
        Trace.Listeners.Clear();
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out) { Name = "console" });
        Trace.AutoFlush = true;

        // Also redirect Console.Error for unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"[FATAL] Unhandled exception: {e.ExceptionObject}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[FATAL] Unobserved task exception: {e.Exception}");
        };

        Console.WriteLine("╔══════════════════════════════════════╗");
        Console.WriteLine("║       YKW Home — Cross-Platform     ║");
        Console.WriteLine("╚══════════════════════════════════════╝");
        Console.WriteLine($"[startup] Version: 1.0.0");
        Console.WriteLine($"[startup] OS: {Environment.OSVersion}");
        Console.WriteLine($"[startup] Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"[startup] Args: [{string.Join(", ", args)}]");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
