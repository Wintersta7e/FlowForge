using System.Threading.Tasks;

namespace FlowForge.UI.Services;

public interface IDialogService
{
    Task<string?> OpenFileAsync(string title, string filter);
    Task<string?> SaveFileAsync(string title, string filter, string? defaultName);
    Task<string?> OpenFolderAsync(string title);
}
