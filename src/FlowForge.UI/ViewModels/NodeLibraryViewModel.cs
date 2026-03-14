using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
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
        Dictionary<string, NodeCategory> categoryKeys = new();

        foreach (string typeKey in registry.GetRegisteredTypeKeys())
        {
            string displayName = registry.GetDisplayName(typeKey);
            NodeCategory category = registry.GetCategoryForTypeKey(typeKey);
            string categoryName = CategoryDisplayNames.GetValueOrDefault(category.ToString(), category.ToString());

            if (!categoryItems.TryGetValue(categoryName, out List<NodeLibraryItemViewModel>? existingItems))
            {
                existingItems = new List<NodeLibraryItemViewModel>();
                categoryItems[categoryName] = existingItems;
                categoryKeys[categoryName] = category;
            }

            string icon = NodeIconMap.Icons.GetValueOrDefault(typeKey, "\u2699");
            (IBrush iconBg, IBrush iconFg) = GetCategoryBrushes(category);
            existingItems.Add(new NodeLibraryItemViewModel(typeKey, displayName, icon, iconBg, iconFg));
        }

        string[] orderedCategories = ["Input", "Process", "Save To"];
        foreach (string cat in orderedCategories)
        {
            if (categoryItems.TryGetValue(cat, out List<NodeLibraryItemViewModel>? items))
            {
                IBrush categoryBrush = GetCategoryHeaderBrush(categoryKeys[cat]);
                NodeLibraryGroupViewModel group = new(cat, new ObservableCollection<NodeLibraryItemViewModel>(items), categoryBrush);
                _allGroups.Add(group);
                Groups.Add(group);
            }
        }
    }

    private static (IBrush IconBg, IBrush IconFg) GetCategoryBrushes(NodeCategory category)
    {
        return category switch
        {
            NodeCategory.Source => (ThemeHelper.GetBrush("ForgeSourceDim", "#145bb8f5"), ThemeHelper.GetBrush("ForgeSource", "#5bb8f5")),
            NodeCategory.Transform => (ThemeHelper.GetBrush("ForgeTransformDim", "#145ce0a0"), ThemeHelper.GetBrush("ForgeTransform", "#5ce0a0")),
            NodeCategory.Output => (ThemeHelper.GetBrush("ForgeOutputDim", "#14e8932f"), ThemeHelper.GetBrush("ForgeOutput", "#e8932f")),
            _ => (ThemeHelper.GetBrush("ForgeElevated", "#252029"), ThemeHelper.GetBrush("ForgeTextMuted", "#564a62"))
        };
    }

    private static IBrush GetCategoryHeaderBrush(NodeCategory category)
    {
        return category switch
        {
            NodeCategory.Source => ThemeHelper.GetBrush("ForgeSource", "#5bb8f5"),
            NodeCategory.Transform => ThemeHelper.GetBrush("ForgeTransform", "#5ce0a0"),
            NodeCategory.Output => ThemeHelper.GetBrush("ForgeOutput", "#e8932f"),
            _ => ThemeHelper.GetBrush("ForgeTextMuted", "#564a62")
        };
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
                // Insert at the correct position to maintain category order
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
