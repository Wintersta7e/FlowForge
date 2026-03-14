using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowForge.UI.Views;

public class ConfigFieldTemplateSelector : IDataTemplate
{
    private ILogger<ConfigFieldTemplateSelector>? _logger;
    private ILogger<ConfigFieldTemplateSelector> Logger =>
        _logger ??= App.Services.GetRequiredService<ILogger<ConfigFieldTemplateSelector>>();

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
            ConfigFieldType.Bool => BuildBoolEditor(field),
            ConfigFieldType.FilePath => BuildFilePathEditor(field),
            ConfigFieldType.FolderPath => BuildFolderPathEditor(field),
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

    private DockPanel BuildFilePathEditor(ConfigFieldViewModel field)
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
                // No user-visible feedback: file picker failure is rare and the dialog simply doesn't appear.
                // The error is logged for diagnostics.
                Logger.LogError(ex, "ConfigFieldTemplateSelector: file picker failed for '{Label}'", field.Label);
            }
        };

        dock.Children.Add(browseButton);
        dock.Children.Add(textBox);
        return dock;
    }

    private DockPanel BuildFolderPathEditor(ConfigFieldViewModel field)
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
                // No user-visible feedback: folder picker failure is rare and the dialog simply doesn't appear.
                // The error is logged for diagnostics.
                Logger.LogError(ex, "ConfigFieldTemplateSelector: folder picker failed for '{Label}'", field.Label);
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
