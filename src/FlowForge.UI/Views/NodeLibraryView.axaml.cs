using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.Views;

public partial class NodeLibraryView : UserControl
{
    public NodeLibraryView()
    {
        InitializeComponent();
    }

    private void OnLibraryItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && control.DataContext is NodeLibraryItemViewModel item)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.Editor.AddNode(item.TypeKey, new Point(300, 200), mainVm.Registry);
            }
        }
    }
}
