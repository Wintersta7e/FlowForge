using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;
    private bool _closePromptOpen;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    // Every close waits for the view model's answer; a close while it is asking is ignored.
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        e.Cancel = true;
        if (_closePromptOpen)
        {
            return;
        }

        _closePromptOpen = true;
        bool close;
        try
        {
            close = await vm.ConfirmCloseAsync();
        }
        finally
        {
            _closePromptOpen = false;
        }

        if (close)
        {
            // Posted: with nothing to ask the answer arrives inside this Closing handler, and
            // closing from there would re-enter it.
            _closeConfirmed = true;
            Dispatcher.UIThread.Post(Close);
        }
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
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
        else if (e.Key == Key.F1)
        {
            var shortcuts = new Views.ShortcutsWindow();
            await shortcuts.ShowDialog(this);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.Editor.Undo();
            e.Handled = true;
        }
        else if ((e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control)
            || (e.Key == Key.Z && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))
        {
            vm.Editor.Redo();
            e.Handled = true;
        }
    }
}
