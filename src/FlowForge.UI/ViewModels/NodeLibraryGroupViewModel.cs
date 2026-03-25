using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

public class NodeLibraryGroupViewModel : ViewModelBase
{
    private readonly List<NodeLibraryItemViewModel> _allItems;

    public string Category { get; }
    public IBrush CategoryBrush { get; }
    public ObservableCollection<NodeLibraryItemViewModel> Items { get; }

    public NodeLibraryGroupViewModel(string category, ObservableCollection<NodeLibraryItemViewModel> items, IBrush categoryBrush)
    {
        Category = category;
        Items = items;
        CategoryBrush = categoryBrush;
        _allItems = new List<NodeLibraryItemViewModel>(items);
    }

    /// <summary>
    /// Filters visible items to those matching <paramref name="search"/>.
    /// Returns true if at least one item matches; false if the group should be hidden.
    /// Passing an empty/null search restores all items.
    /// </summary>
    public bool ApplyFilter(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            // Restore all items, avoiding unnecessary churn
            if (Items.Count == _allItems.Count)
            {
                return true;
            }

            Items.Clear();
            foreach (NodeLibraryItemViewModel item in _allItems)
            {
                Items.Add(item);
            }

            return true;
        }

        var matching = _allItems
            .Where(item => item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Items.Clear();
        foreach (NodeLibraryItemViewModel item in matching)
        {
            Items.Add(item);
        }

        return matching.Count > 0;
    }
}
