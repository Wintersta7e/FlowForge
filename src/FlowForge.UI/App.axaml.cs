using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FlowForge.Core.DependencyInjection;
using FlowForge.UI.Services;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FlowForge.UI;

public partial class App : Application
{
    private static IServiceProvider? _services;

    public static IServiceProvider Services => _services
        ?? throw new InvalidOperationException("DI container not initialized. Application startup failed.");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                path: "logs/flowforge.log",
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10_485_760)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog(dispose: true));
        services.AddFlowForgeCore();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<EditorViewModel>();
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) =>
            {
                try
                {
                    if (_services is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine($"Error during shutdown disposal: {ex}");
                }

                Log.CloseAndFlush();
            };

            MainWindowViewModel viewModel = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // Fire-and-forget: window shows immediately, RecentPipelines updates when ready
            _ = InitializeViewModelAsync(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async System.Threading.Tasks.Task InitializeViewModelAsync(MainWindowViewModel viewModel)
    {
        ILogger<App> logger = Services.GetRequiredService<ILogger<App>>();
        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize application settings");
            viewModel.ExecutionLog.Summary = "Warning: Could not load settings. Using defaults.";
        }
    }
}
