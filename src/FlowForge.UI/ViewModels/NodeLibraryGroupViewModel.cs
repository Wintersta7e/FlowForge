using System.Collections.ObjectModel;

namespace FlowForge.UI.ViewModels;

public class NodeLibraryGroupViewModel : ViewModelBase
{
    public string Category { get; }
    public ObservableCollection<NodeLibraryItemViewModel> Items { get; }

    public NodeLibraryGroupViewModel(string category, ObservableCollection<NodeLibraryItemViewModel> items)
    {
        Category = category;
        Items = items;
    }
}
