using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.Views;

public partial class ToolbarView : UserControl
{
    public ToolbarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.RecentPipelines))
                {
                    RebuildRecentMenu(vm);
                }
            };

            RebuildRecentMenu(vm);
        }
    }

    private void OnFitToScreenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Editor.RequestFitToScreen();
        }
    }

    private void RebuildRecentMenu(MainWindowViewModel vm)
    {
        if (RecentButton?.Flyout is not MenuFlyout flyout)
        {
            return;
        }

        flyout.Items.Clear();

        if (vm.RecentPipelines.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "No recent pipelines", IsEnabled = false });
            return;
        }

        foreach (string path in vm.RecentPipelines)
        {
            string fileName = Path.GetFileName(path);
            var item = new MenuItem
            {
                Header = fileName,
                Tag = path,
            };

            ToolTip.SetTip(item, path);
            item.Click += (_, _) => vm.OpenRecentCommand.Execute(path);
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "Clear Recent" };
        clearItem.Click += (_, _) => vm.ClearRecentCommand.Execute(null);
        flyout.Items.Add(clearItem);
    }
}
