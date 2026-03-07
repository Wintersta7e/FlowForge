using System.Collections.ObjectModel;
using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

public class NodeLibraryGroupViewModel : ViewModelBase
{
    public string Category { get; }
    public IBrush CategoryBrush { get; }
    public ObservableCollection<NodeLibraryItemViewModel> Items { get; }

    public NodeLibraryGroupViewModel(string category, ObservableCollection<NodeLibraryItemViewModel> items, IBrush categoryBrush)
    {
        Category = category;
        Items = items;
        CategoryBrush = categoryBrush;
    }
}
