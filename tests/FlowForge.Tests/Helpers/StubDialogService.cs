using FlowForge.UI.Services;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// The test dialog service: each member returns the result the test set (null by default;
/// <see cref="UnsavedChangesChoice.Discard"/> for the unsaved-changes prompt) and records its
/// own name, in call order.
/// </summary>
internal sealed class StubDialogService : IDialogService
{
    private readonly List<string> _calls = [];

    public string? OpenFileResult { get; set; }

    public string? SaveFileResult { get; set; }

    public string? OpenFolderResult { get; set; }

    public UnsavedChangesChoice ConfirmResult { get; set; } = UnsavedChangesChoice.Discard;

    // When set, the prompt stays open until the test completes this answer.
    public TaskCompletionSource<UnsavedChangesChoice>? PendingConfirm { get; set; }

    public string? LastPipelineName { get; private set; }

    public IReadOnlyList<string> Calls => _calls;

    public Task<string?> OpenFileAsync(string title, string filter)
    {
        _calls.Add(nameof(OpenFileAsync));
        return Task.FromResult(OpenFileResult);
    }

    public Task<string?> SaveFileAsync(string title, string filter, string? defaultName)
    {
        _calls.Add(nameof(SaveFileAsync));
        return Task.FromResult(SaveFileResult);
    }

    public Task<string?> OpenFolderAsync(string title)
    {
        _calls.Add(nameof(OpenFolderAsync));
        return Task.FromResult(OpenFolderResult);
    }

    public Task<UnsavedChangesChoice> ConfirmUnsavedChangesAsync(string pipelineName)
    {
        _calls.Add(nameof(ConfirmUnsavedChangesAsync));
        LastPipelineName = pipelineName;
        return PendingConfirm?.Task ?? Task.FromResult(ConfirmResult);
    }
}
