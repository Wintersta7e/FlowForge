using Avalonia.Controls;
using Avalonia.VisualTree;
using FlowForge.UI.Views;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The rows a shown shortcuts dialog lists: each row's key text and description, in row order.
/// </summary>
internal static class ShortcutRows
{
    public static IReadOnlyList<(string Key, string Description)> Read(ShortcutsWindow window)
    {
        var cells = window.GetVisualDescendants().OfType<TextBlock>().ToList();
        Dictionary<int, string> keys = Column(cells, "key");
        Dictionary<int, string> descriptions = Column(cells, "desc");
        if (!keys.Keys.Order().SequenceEqual(descriptions.Keys.Order()))
        {
            throw new InvalidOperationException(
                $"Key rows [{string.Join(", ", keys.Keys.Order())}] and description rows [{string.Join(", ", descriptions.Keys.Order())}] differ.");
        }

        return keys.Keys.Order().Select(row => (keys[row], descriptions[row])).ToList();
    }

    // The text of each TextBlock with this class, by its grid row.
    private static Dictionary<int, string> Column(List<TextBlock> cells, string cellClass) =>
        cells.Where(cell => cell.Classes.Contains(cellClass))
            .ToDictionary(Grid.GetRow, cell => cell.Text ?? string.Empty);
}
