using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.ViewModels;
using Serilog;

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
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1C2128")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#30363D")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(6),
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
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6EDF3")),
            Margin = new Avalonia.Thickness(0, 0, 0, 2)
        };
        panel.Children.Add(label);

        Control editor = field.FieldType switch
        {
            ConfigFieldType.String => BuildStringEditor(field),
            ConfigFieldType.Int => BuildIntEditor(field),
            ConfigFieldType.Bool => BuildBoolEditor(field),
            ConfigFieldType.FilePath => BuildFilePathEditor(field),
            ConfigFieldType.FolderPath => BuildFolderPathEditor(field),
            ConfigFieldType.Select => BuildSelectEditor(field),
            ConfigFieldType.MultiLine => BuildMultiLineEditor(field),
            _ => new TextBlock { Text = $"Unsupported field type: {field.FieldType}" }
        };

        panel.Children.Add(editor);
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
            Watermark = field.Placeholder ?? "0"
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));
        return textBox;
    }

    private static ToggleSwitch BuildBoolEditor(ConfigFieldViewModel field)
    {
        ToggleSwitch toggle = new()
        {
            OnContent = "Yes",
            OffContent = "No"
        };
        // Bind IsChecked via a converter or handle in code;
        // for simplicity, bind to Value as string "True"/"False"
        toggle.Bind(ToggleSwitch.IsCheckedProperty,
            new Avalonia.Data.Binding("Value")
            {
                Converter = BoolStringConverter.Instance
            });
        return toggle;
    }

    private static DockPanel BuildFilePathEditor(ConfigFieldViewModel field)
    {
        DockPanel dock = new();

        Button browseButton = new()
        {
            Content = "...",
            Width = 32,
            Margin = new Avalonia.Thickness(4, 0, 0, 0)
        };
        DockPanel.SetDock(browseButton, Dock.Right);

        TextBox textBox = new()
        {
            Watermark = field.Placeholder ?? "Select file..."
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));

        browseButton.Click += async (_, _) =>
        {
            try
            {
                IStorageProvider? storage = GetStorageProvider();
                if (storage is null)
                    return;

                System.Collections.Generic.IReadOnlyList<IStorageFile> result =
                    await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = field.Label,
                        AllowMultiple = false
                    });

                if (result.Count > 0)
                {
                    field.Value = result[0].Path.LocalPath;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ConfigFieldTemplateSelector: file picker failed for '{Label}'", field.Label);
            }
        };

        dock.Children.Add(browseButton);
        dock.Children.Add(textBox);
        return dock;
    }

    private static DockPanel BuildFolderPathEditor(ConfigFieldViewModel field)
    {
        DockPanel dock = new();

        Button browseButton = new()
        {
            Content = "...",
            Width = 32,
            Margin = new Avalonia.Thickness(4, 0, 0, 0)
        };
        DockPanel.SetDock(browseButton, Dock.Right);

        TextBox textBox = new()
        {
            Watermark = field.Placeholder ?? "Select folder..."
        };
        textBox.Bind(TextBox.TextProperty,
            new Avalonia.Data.Binding("Value"));

        browseButton.Click += async (_, _) =>
        {
            try
            {
                IStorageProvider? storage = GetStorageProvider();
                if (storage is null)
                    return;

                System.Collections.Generic.IReadOnlyList<IStorageFolder> result =
                    await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = field.Label,
                        AllowMultiple = false
                    });

                if (result.Count > 0)
                {
                    field.Value = result[0].Path.LocalPath;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ConfigFieldTemplateSelector: folder picker failed for '{Label}'", field.Label);
            }
        };

        dock.Children.Add(browseButton);
        dock.Children.Add(textBox);
        return dock;
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is not null)
        {
            return desktop.MainWindow.StorageProvider;
        }
        return null;
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
