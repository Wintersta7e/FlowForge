using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace FlowForge.UI.Services;

public class DialogService : IDialogService
{
    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.StorageProvider;
        }

        return null;
    }

    public async Task<string?> OpenFileAsync(string title, string filter)
    {
        IStorageProvider? storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        FilePickerOpenOptions options = new()
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = ParseFilter(filter)
        };

        IReadOnlyList<IStorageFile> result = await storageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(string title, string filter, string? defaultName)
    {
        IStorageProvider? storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        FilePickerSaveOptions options = new()
        {
            Title = title,
            SuggestedFileName = defaultName,
            FileTypeChoices = ParseFilter(filter)
        };

        IStorageFile? result = await storageProvider.SaveFilePickerAsync(options);
        return result?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        IStorageProvider? storageProvider = GetStorageProvider();
        if (storageProvider is null)
        {
            return null;
        }

        FolderPickerOpenOptions options = new()
        {
            Title = title,
            AllowMultiple = false
        };

        IReadOnlyList<IStorageFolder> result = await storageProvider.OpenFolderPickerAsync(options);
        return result.Count > 0 ? result[0].Path.LocalPath : null;
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
