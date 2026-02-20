using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;

namespace FlowForge.UI.ViewModels;

public partial class NodeLibraryViewModel : ViewModelBase
{
    private static readonly Dictionary<string, string> CategoryDisplayNames = new()
    {
        ["Source"] = "Input",
        ["Transform"] = "Process",
        ["Output"] = "Save To"
    };

    private List<NodeLibraryGroupViewModel> _allGroups = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    public ObservableCollection<NodeLibraryGroupViewModel> Groups { get; } = new();

    partial void OnSearchTextChanged(string value)
    {
        FilterItems();
    }

    public void Initialize(NodeRegistry registry)
    {
        _allGroups.Clear();
        Groups.Clear();

        Dictionary<string, List<NodeLibraryItemViewModel>> categoryItems = new();

        foreach (string typeKey in registry.GetRegisteredTypeKeys())
        {
            string displayName = registry.GetDisplayName(typeKey);
            NodeCategory category = registry.GetCategoryForTypeKey(typeKey);
            string categoryName = CategoryDisplayNames.GetValueOrDefault(category.ToString(), category.ToString());

            if (!categoryItems.TryGetValue(categoryName, out List<NodeLibraryItemViewModel>? existingItems))
            {
                existingItems = new List<NodeLibraryItemViewModel>();
                categoryItems[categoryName] = existingItems;
            }

            existingItems.Add(new NodeLibraryItemViewModel(typeKey, displayName));
        }

        // Add groups in canonical order: Input, Process, Save To
        string[] orderedCategories = ["Input", "Process", "Save To"];
        foreach (string cat in orderedCategories)
        {
            if (categoryItems.TryGetValue(cat, out List<NodeLibraryItemViewModel>? items))
            {
                NodeLibraryGroupViewModel group = new(cat, new ObservableCollection<NodeLibraryItemViewModel>(items));
                _allGroups.Add(group);
                Groups.Add(group);
            }
        }
    }

    private void FilterItems()
    {
        Groups.Clear();

        string search = SearchText.Trim();

        foreach (NodeLibraryGroupViewModel group in _allGroups)
        {
            if (string.IsNullOrEmpty(search))
            {
                Groups.Add(group);
                continue;
            }

            List<NodeLibraryItemViewModel> filtered = group.Items
                .Where(item => item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (filtered.Count > 0)
            {
                Groups.Add(new NodeLibraryGroupViewModel(
                    group.Category,
                    new ObservableCollection<NodeLibraryItemViewModel>(filtered)));
            }
        }
    }
}
