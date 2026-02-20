using Avalonia.Controls;
using Avalonia.Input;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataContext is MainWindowViewModel vm)
        {
            vm.Editor.RemoveSelectedNodes();
        }
    }
}
