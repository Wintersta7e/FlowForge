using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.Views;

public partial class CanvasView : UserControl
{
    private Action? _fitToScreenHandler;
    private EditorViewModel? _subscribedEditor;

    public CanvasView()
    {
        InitializeComponent();

        // Wire drag-drop on the Nodify editor
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        // Unsubscribe from previous editor to prevent handler accumulation
        if (_subscribedEditor is not null && _fitToScreenHandler is not null)
        {
            _subscribedEditor.FitToScreenRequested -= _fitToScreenHandler;
        }

        _subscribedEditor = null;
        _fitToScreenHandler = null;

        if (DataContext is EditorViewModel editor)
        {
            _fitToScreenHandler = () =>
                Nodify.Avalonia.EditorCommands.FitToScreen.Execute(parameter: null!, Editor);
            editor.FitToScreenRequested += _fitToScreenHandler;
            _subscribedEditor = editor;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        string? text = e.DataTransfer.TryGetText();
        if (text is not null)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        string? typeKey = e.DataTransfer.TryGetText();
        if (typeKey is null)
            return;

        // Convert screen position to canvas position
        Point screenPos = e.GetPosition(Editor);
        Point canvasPos = new(
            (screenPos.X / Editor.ViewportZoom) + Editor.ViewportLocation.X,
            (screenPos.Y / Editor.ViewportZoom) + Editor.ViewportLocation.Y
        );

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.DataContext is MainWindowViewModel mainVm)
        {
            mainVm.Editor.AddNode(typeKey, canvasPos, mainVm.Registry);
        }
    }

    private void OnTemplateButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string templateId)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.DataContext is MainWindowViewModel mainVm)
            {
                mainVm.LoadTemplateCommand.Execute(templateId);
            }
        }
    }
}
