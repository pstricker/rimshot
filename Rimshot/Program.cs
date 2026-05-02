using Avalonia;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Rimshot;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void LogCrash(Exception? ex)
    {
        if (ex is null) return;

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Rimshot");
            Directory.CreateDirectory(dir);

            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "dev";

            var entry =
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz} ===" + Environment.NewLine +
                $"Rimshot {version} on {Environment.OSVersion} ({Environment.OSVersion.Platform})" + Environment.NewLine +
                ex + Environment.NewLine + Environment.NewLine;

            File.AppendAllText(Path.Combine(dir, "crash.log"), entry);
        }
        catch
        {
            // Crash handler must never throw — if logging fails, give up silently.
        }
    }
}
