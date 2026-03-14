using Avalonia;
using System;

namespace FlowForge.UI;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string crashLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            System.IO.File.WriteAllText(crashLog, $"{DateTime.Now:O}\n{e.ExceptionObject}");
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            string crashLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            System.IO.File.WriteAllText(crashLog, $"{DateTime.Now:O}\n{ex}");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
