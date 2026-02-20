using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.Views;

public partial class NodeLibraryView : UserControl
{
    private Point? _dragStartPoint;
    private NodeLibraryItemViewModel? _dragItem;
    private bool _isDragging;
    private const double DragThreshold = 6.0;

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
        e.Handled = true;
    }

    private void OnLibraryItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDragging)
            return;

        if (sender is Control control && control.DataContext is NodeLibraryItemViewModel item)
        {
            PointerPoint point = e.GetCurrentPoint(control);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            _dragStartPoint = e.GetPosition(this);
            _dragItem = item;
        }
    }

    private async void OnLibraryItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint is null || _dragItem is null || _isDragging)
            return;

        Point currentPoint = e.GetPosition(this);
        Vector delta = currentPoint - _dragStartPoint.Value;

        if (Math.Abs(delta.X) >= DragThreshold || Math.Abs(delta.Y) >= DragThreshold)
        {
            _isDragging = true;
            NodeLibraryItemViewModel item = _dragItem;
            _dragStartPoint = null;
            _dragItem = null;

            DataTransfer data = new();
            data.Add(DataTransferItem.CreateText(item.TypeKey));

            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
            _isDragging = false;
        }
    }

    private void OnLibraryItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStartPoint = null;
        _dragItem = null;
    }
}
