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
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            vm.Editor.RemoveSelectedNodes();
            e.Handled = true;
        }
        else if (e.Key == Key.D0 && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.Editor.RequestFitToScreen();
            e.Handled = true;
        }
    }
}
