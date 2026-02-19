using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FlowForge.UI.Views;

/// <summary>
/// Converts between a nullable bool and a string representation ("True"/"False").
/// Used by ConfigFieldTemplateSelector for Bool field types.
/// </summary>
public sealed class BoolStringConverter : IValueConverter
{
    public static readonly BoolStringConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.Equals(str, "True", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "True" : "False";
        }

        return "False";
    }
}
