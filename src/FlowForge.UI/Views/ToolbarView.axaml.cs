using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.Views;

public partial class ToolbarView : UserControl
{
    private MainWindowViewModel? _subscribedVm;
    private PropertyChangedEventHandler? _vmPropertyChangedHandler;

    public ToolbarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // Unsubscribe from previous VM to prevent handler accumulation
        if (_subscribedVm is not null && _vmPropertyChangedHandler is not null)
        {
            _subscribedVm.PropertyChanged -= _vmPropertyChangedHandler;
        }

        _subscribedVm = null;
        _vmPropertyChangedHandler = null;

        if (DataContext is MainWindowViewModel vm)
        {
            _vmPropertyChangedHandler = (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.RecentPipelineItems))
                {
                    RebuildRecentMenu(vm);
                }
            };
            _subscribedVm = vm;
            vm.PropertyChanged += _vmPropertyChangedHandler;

            RebuildRecentMenu(vm);
        }
    }

    private async void OnShowShortcutsClick(object? sender, RoutedEventArgs e)
    {
        var window = new ShortcutsWindow();
        Window? topLevel = TopLevel.GetTopLevel(this) as Window;
        if (topLevel is not null)
        {
            await window.ShowDialog(topLevel);
        }
    }

    private void OnFitToScreenClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Editor.RequestFitToScreen();
        }
    }

    private void OnRecentItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string path
            && DataContext is MainWindowViewModel vm)
        {
            vm.OpenRecentCommand.Execute(path);
        }
    }

    private void OnClearRecentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ClearRecentCommand.Execute(null);
        }
    }

    private void RebuildRecentMenu(MainWindowViewModel vm)
    {
        if (RecentButton?.Flyout is not MenuFlyout flyout)
        {
            return;
        }

        // Unsubscribe Click handlers from existing items to prevent leaks
        foreach (object? existingItem in flyout.Items)
        {
            if (existingItem is MenuItem menuItem)
            {
                menuItem.Click -= OnRecentItemClick;
                menuItem.Click -= OnClearRecentClick;
            }
        }

        flyout.Items.Clear();

        if (vm.RecentPipelineItems.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "No recent pipelines", IsEnabled = false });
            return;
        }

        foreach (ViewModels.RecentPipelineItem recent in vm.RecentPipelineItems)
        {
            var item = new MenuItem
            {
                Header = recent.FileName,
                Tag = recent.FullPath,
            };

            ToolTip.SetTip(item, recent.FullPath);
            item.Click += OnRecentItemClick;
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new Separator());
        var clearItem = new MenuItem { Header = "Clear Recent" };
        clearItem.Click += OnClearRecentClick;
        flyout.Items.Add(clearItem);
    }
}
