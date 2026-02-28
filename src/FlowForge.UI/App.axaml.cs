using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FlowForge.UI.ViewModels;
using Serilog;

namespace FlowForge.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
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
        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize application settings");
        }
    }
}
