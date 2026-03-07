using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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

    private static readonly Dictionary<string, string> NodeIcons = new()
    {
        ["FolderInput"] = "\U0001F4C1",
        ["RenamePattern"] = "\u270E",
        ["RenameRegex"] = ".*",
        ["RenameAddAffix"] = "+a",
        ["Filter"] = "\U0001F50D",
        ["Sort"] = "\u21C5",
        ["ImageResize"] = "\U0001F4F8",
        ["ImageConvert"] = "\U0001F3A8",
        ["ImageCompress"] = "\U0001F4E6",
        ["MetadataExtract"] = "\U0001F4C4",
        ["FolderOutput"] = "\U0001F4E5",
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

            string icon = NodeIcons.GetValueOrDefault(typeKey, "\u2699");
            var brushes = GetCategoryBrushes(category);
            existingItems.Add(new NodeLibraryItemViewModel(typeKey, displayName, icon, brushes.IconBg, brushes.IconFg));
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
            NodeCategory.Source => (GetBrush("ForgeSourceDim", "#145bb8f5"), GetBrush("ForgeSource", "#5bb8f5")),
            NodeCategory.Transform => (GetBrush("ForgeTransformDim", "#145ce0a0"), GetBrush("ForgeTransform", "#5ce0a0")),
            NodeCategory.Output => (GetBrush("ForgeOutputDim", "#14e8932f"), GetBrush("ForgeOutput", "#e8932f")),
            _ => (GetBrush("ForgeElevated", "#252029"), GetBrush("ForgeTextMuted", "#564a62"))
        };
    }

    private static IBrush GetCategoryHeaderBrush(NodeCategory category)
    {
        return category switch
        {
            NodeCategory.Source => GetBrush("ForgeSource", "#5bb8f5"),
            NodeCategory.Transform => GetBrush("ForgeTransform", "#5ce0a0"),
            NodeCategory.Output => GetBrush("ForgeOutput", "#e8932f"),
            _ => GetBrush("ForgeTextMuted", "#564a62")
        };
    }

    private static IBrush GetBrush(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out object? resource) == true && resource is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallback));
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
                    new ObservableCollection<NodeLibraryItemViewModel>(filtered),
                    group.CategoryBrush));
            }
        }
    }
}
