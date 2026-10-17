using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using FlowForge.UI.Views;
using Microsoft.Extensions.Logging;

namespace FlowForge.UI.Services;

public class DialogService : IDialogService
{
    private readonly ILogger<DialogService> _logger;
    private readonly Func<IStorageProvider?> _storageProvider;

    public DialogService(ILogger<DialogService> logger)
        : this(logger, () => GetMainWindow()?.StorageProvider)
    {
    }

    internal DialogService(ILogger<DialogService> logger, Func<IStorageProvider?> storageProvider)
    {
        _logger = logger;
        _storageProvider = storageProvider;
    }

    private static Window? GetMainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync(string pipelineName)
    {
        Window? owner = GetMainWindow();
        if (owner is null)
        {
            return UnsavedChangesChoice.Cancel;
        }

        // Closing the prompt by its frame picks no button: nothing is lost.
        UnsavedChangesWindow prompt = new() { DataContext = pipelineName };
        UnsavedChangesChoice? choice = await prompt.ShowDialog<UnsavedChangesChoice?>(owner);
        return choice ?? UnsavedChangesChoice.Cancel;
    }

    public Task<string?> OpenFileAsync(string title, string filter) =>
        PickAsync(title, async storageProvider =>
        {
            FilePickerOpenOptions options = new()
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = ParseFilter(filter)
            };

            IReadOnlyList<IStorageFile> result = await storageProvider.OpenFilePickerAsync(options);
            return result.Count > 0 ? result[0].Path.LocalPath : null;
        });

    public Task<string?> SaveFileAsync(string title, string filter, string? defaultName) =>
        PickAsync(title, async storageProvider =>
        {
            FilePickerSaveOptions options = new()
            {
                Title = title,
                SuggestedFileName = defaultName,
                FileTypeChoices = ParseFilter(filter)
            };

            IStorageFile? result = await storageProvider.SaveFilePickerAsync(options);
            return result?.Path.LocalPath;
        });

    public Task<string?> OpenFolderAsync(string title) =>
        PickAsync(title, async storageProvider =>
        {
            FolderPickerOpenOptions options = new()
            {
                Title = title,
                AllowMultiple = false
            };

            IReadOnlyList<IStorageFolder> result = await storageProvider.OpenFolderPickerAsync(options);
            return result.Count > 0 ? result[0].Path.LocalPath : null;
        });

    // A dialog that fails to open is logged and treated as closed without a choice, so the
    // command that asked for it stops instead of taking the app down.
    private async Task<string?> PickAsync(string title, Func<IStorageProvider, Task<string?>> pick)
    {
        IStorageProvider? storageProvider = _storageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        try
        {
            return await pick(storageProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The '{Title}' dialog failed to open", title);
            return null;
        }
    }

    private static List<FilePickerFileType> ParseFilter(string filter)
    {
        List<FilePickerFileType> fileTypes = new();

        // Filter format: "Pipeline Files|*.ffpipe|All Files|*.*"
        string[] parts = filter.Split('|');
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string name = parts[i].Trim();
            string[] patterns = parts[i + 1].Split(';')
                .Select(p => p.Trim())
                .ToArray();

            fileTypes.Add(new FilePickerFileType(name) { Patterns = patterns });
        }

        return fileTypes;
    }
}
