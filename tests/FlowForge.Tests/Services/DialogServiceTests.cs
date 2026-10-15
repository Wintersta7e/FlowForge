using System.Reflection;
using System.Text.Json;
using Avalonia.Platform.Storage;
using FluentAssertions;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using FlowForge.UI.Services;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace FlowForge.Tests.Services;

public class DialogServiceTests
{
    /// <summary>
    /// A system dialog that fails to open is logged and reads as one closed without a choice, so
    /// Open, Save and Save As stop instead of ending the app.
    /// </summary>
    [Theory]
    [InlineData(nameof(DialogService.OpenFileAsync))]
    [InlineData(nameof(DialogService.SaveFileAsync))]
    [InlineData(nameof(DialogService.OpenFolderAsync))]
    public async Task PickerFails_LogsAndReturnsNull(string picker)
    {
        var logger = new ListLogger();
        var dialogs = new DialogService(logger, FailingStorageProvider.Create);
        Func<Task<string?>> pick = picker switch
        {
            nameof(DialogService.OpenFileAsync) => () => dialogs.OpenFileAsync("Open Pipeline", "Pipeline Files|*.ffpipe"),
            nameof(DialogService.SaveFileAsync) => () => dialogs.SaveFileAsync("Save Pipeline", "Pipeline Files|*.ffpipe", "Untitled.ffpipe"),
            _ => () => dialogs.OpenFolderAsync("Input Folder"),
        };

        string? path = "unset";
        Func<Task> act = async () => path = await pick();

        await act.Should().NotThrowAsync("a dialog that fails to open must not end the app");
        path.Should().BeNull("a failed dialog reads as one closed without a choice");
        logger.Errors.Should().ContainSingle("the failure is logged once")
            .Which.Should().BeOfType<InvalidOperationException>();
    }

    /// <summary>
    /// A path field's browse button whose dialog fails to open leaves the field, its config and
    /// the history as they were, and the failure is logged.
    /// </summary>
    [Fact]
    public async Task Browse_PickerFails_LogsAndChangesNothing()
    {
        var logger = new ListLogger();
        var dialogs = new DialogService(logger, FailingStorageProvider.Create);
        var field = new ConfigField("path", ConfigFieldType.FolderPath, Label: "Input Folder");
        Dictionary<string, JsonElement> config = ConfigHelper.MakeConfig(("path", "kept"));
        var received = new List<IUndoableCommand>();
        var vm = new ConfigFieldViewModel(field, config, dialogs, received.Add);

        Func<Task> browse = () => vm.BrowseCommand.ExecuteAsync(null);

        await browse.Should().NotThrowAsync("a browse button whose dialog fails must not end the app");
        vm.Value.Should().Be("kept");
        config["path"].GetString().Should().Be("kept", "a failed dialog changes nothing");
        received.Should().BeEmpty("a failed dialog pushes no undo step");
        logger.Errors.Should().ContainSingle("the failure is logged once");
    }

    private sealed class ListLogger : ILogger<DialogService>
    {
        public List<Exception?> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(exception);
            }
        }
    }

    // Avalonia lets no user code implement IStorageProvider, so the failing provider is a runtime
    // proxy: every member throws, the way a platform without a usable dialog does.
    public class FailingStorageProvider : DispatchProxy
    {
        public static IStorageProvider Create() => Create<IStorageProvider, FailingStorageProvider>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("The system dialog could not be shown.");
    }
}
