using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;

namespace FlowForge.UI.ViewModels;

public partial class NodeLibraryViewModel : ViewModelBase
{
    private static readonly Dictionary<string, string> CategoryDisplayNames = new(StringComparer.Ordinal)
    {
        ["Source"] = "Input",
        ["Transform"] = "Process",
        ["Output"] = "Save To",
    };

    private readonly List<NodeLibraryGroupViewModel> _allGroups = new();

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

        Dictionary<string, List<NodeLibraryItemViewModel>> categoryItems = new(StringComparer.Ordinal);

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
        string search = SearchText.Trim();

        // Reuse existing group VMs: apply filter in-place and toggle visibility
        // by adding/removing from Groups rather than recreating group instances.
        foreach (NodeLibraryGroupViewModel group in _allGroups)
        {
            bool hasMatches = group.ApplyFilter(search);
            bool isVisible = Groups.Contains(group);

            if (hasMatches && !isVisible)
            {
                int insertIndex = 0;
                foreach (NodeLibraryGroupViewModel existing in _allGroups)
                {
                    if (existing == group)
                    {
                        break;
                    }

                    if (Groups.Contains(existing))
                    {
                        insertIndex++;
                    }
                }

                Groups.Insert(insertIndex, group);
            }
            else if (!hasMatches && isVisible)
            {
                Groups.Remove(group);
            }
        }
    }
}
