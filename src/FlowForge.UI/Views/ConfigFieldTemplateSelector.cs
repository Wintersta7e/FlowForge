using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.Views;

public class ConfigFieldTemplateSelector : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is not ConfigFieldViewModel field)
        {
            return new TextBlock { Text = "Unknown field" };
        }

        Border cardBorder = new()
        {
            Background = ThemeHelper.GetBrush("ForgeDeep", "#0e0c11"),
            BorderBrush = ThemeHelper.GetBrush("ForgeBorder", "#2a2230"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(12),
            Padding = new Avalonia.Thickness(12, 8),
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };

        StackPanel panel = new()
        {
            Spacing = 4
        };

        TextBlock label = new()
        {
            Text = field.IsRequired ? $"{field.Label} *" : field.Label,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            FontSize = 12,
            Foreground = ThemeHelper.GetBrush("ForgeText", "#ede6f0"),
            Margin = new Avalonia.Thickness(0, 0, 0, 2)
        };
        panel.Children.Add(label);

        Control editor = field.FieldType switch
        {
            ConfigFieldType.String => BuildStringEditor(field),
            ConfigFieldType.Int => BuildIntEditor(field),
            ConfigFieldType.Bool => BuildBoolEditor(),
            ConfigFieldType.FilePath => BuildPathEditor(field, "Select file..."),
            ConfigFieldType.FolderPath => BuildPathEditor(field, "Select folder..."),
            ConfigFieldType.Select => BuildSelectEditor(field),
            ConfigFieldType.MultiLine => BuildMultiLineEditor(field),
            _ => new TextBlock { Text = $"Unsupported field type: {field.FieldType}" }
        };

        panel.Children.Add(editor);

        if (field.Tooltip is not null)
        {
            ToolTip.SetTip(cardBorder, field.Tooltip);
        }

        cardBorder.Child = panel;
        return cardBorder;
    }

    public bool Match(object? data)
    {
        return data is ConfigFieldViewModel;
    }

    private static TextBox BuildStringEditor(ConfigFieldViewModel field)
    {
        TextBox textBox = new()
        {
            Watermark = field.Placeholder ?? string.Empty
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));
        return textBox;
    }

    private static TextBox BuildIntEditor(ConfigFieldViewModel field)
    {
        TextBox textBox = new()
        {
            Watermark = field.Placeholder ?? field.DefaultValue ?? string.Empty
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));
        textBox.BindClass("error", new Avalonia.Data.Binding(nameof(ConfigFieldViewModel.HasError)), textBox);
        return textBox;
    }

    // The card's label above names the field, so the box carries no text of its own.
    private static CheckBox BuildBoolEditor()
    {
        CheckBox checkBox = new();
        checkBox.Bind(CheckBox.IsCheckedProperty,
            new Avalonia.Data.Binding("Value")
            {
                Converter = BoolStringConverter.Instance
            });
        return checkBox;
    }

    // The "..." button runs the field's browse command, which asks the dialog service for a path.
    private static DockPanel BuildPathEditor(ConfigFieldViewModel field, string watermark)
    {
        DockPanel dock = new();

        Button browseButton = new()
        {
            Content = "...",
            Width = 32,
            Margin = new Avalonia.Thickness(4, 0, 0, 0)
        };
        DockPanel.SetDock(browseButton, Dock.Right);
        browseButton.Bind(Button.CommandProperty,
            new Avalonia.Data.Binding(nameof(ConfigFieldViewModel.BrowseCommand)));

        TextBox textBox = new()
        {
            Watermark = field.Placeholder ?? watermark
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));

        dock.Children.Add(browseButton);
        dock.Children.Add(textBox);
        return dock;
    }

    private static ComboBox BuildSelectEditor(ConfigFieldViewModel field)
    {
        ComboBox comboBox = new()
        {
            ItemsSource = field.Options,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        comboBox.Bind(ComboBox.SelectedItemProperty,
            new Avalonia.Data.Binding("Value"));
        return comboBox;
    }

    private static TextBox BuildMultiLineEditor(ConfigFieldViewModel field)
    {
        TextBox textBox = new()
        {
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Height = 80,
            Watermark = field.Placeholder ?? string.Empty
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));
        return textBox;
    }
}
