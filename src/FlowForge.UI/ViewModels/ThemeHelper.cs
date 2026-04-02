using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

internal static class ThemeHelper
{
    public static IBrush GetBrush(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out object? resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallback));
    }

    public static Color GetColor(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out object? resource) == true && resource is Color color)
        {
            return color;
        }

        return Color.Parse(fallback);
    }
}
