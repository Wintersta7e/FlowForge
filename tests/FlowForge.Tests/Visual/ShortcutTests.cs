using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlowForge.Core.Pipeline;
using FlowForge.UI;
using FlowForge.UI.Services;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The keys the shortcuts dialog lists reach the commands they name, and Save As is on the
/// toolbar as well as on its key.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ShortcutTests
{
    private const string SaveDialog = nameof(IDialogService.SaveFileAsync);
    private const string OpenDialog = nameof(IDialogService.OpenFileAsync);
    private const string Prompt = nameof(IDialogService.ConfirmUnsavedChangesAsync);

    private readonly ITestOutputHelper _output;

    public ShortcutTests(ITestOutputHelper output) => _output = output;

    [AvaloniaTheory]
    [InlineData("Ctrl+N")]
    [InlineData("Ctrl+O")]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+Shift+S")]
    public void FileShortcuts_InvokeTheirCommands(string gesture)
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        IReadOnlyList<(string Key, string Description)> rows = ShownShortcuts(session);
        rows.Select(row => row.Key).Should().Contain(gesture, "the shortcuts dialog lists the key");

        switch (gesture)
        {
            case "Ctrl+N":
                session.LoadTemplate(VisualSession.WebExportTemplate);
                session.Press(gesture);
                session.RenderUntil(() => vm.Editor.Nodes.Count == 0, "the loaded template cleared");
                Count(session, Prompt).Should().Be(1, "New asks before dropping the unsaved template");
                break;

            case "Ctrl+O":
                session.Press(gesture);
                session.RenderUntil(() => Count(session, OpenDialog) == 1, "a file asked for");
                break;

            case "Ctrl+S":
                session.LoadTemplate(VisualSession.WebExportTemplate);
                string path = Path.Combine(session.Temp.Path, "saved-by-key.ffpipe");
                File.WriteAllText(path, "not yet saved");
                vm.CurrentFilePath = path;
                session.Press(gesture);
                session.RenderUntil(() => !vm.IsDirty, "the pipeline saved");
                PipelineGraph saved = PipelineSerializer.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                _output.WriteLine($"saved: nodes={saved.Nodes.Count} editor={vm.Editor.Nodes.Count}");
                saved.Nodes.Should().HaveCount(vm.Editor.Nodes.Count, "the open file is rewritten with the pipeline");
                Count(session, SaveDialog).Should().Be(0, "a pipeline with a file saves to it without asking");
                break;

            case "Ctrl+Shift+S":
                session.Press(gesture);
                session.RenderUntil(() => Count(session, SaveDialog) == 1, "a file asked for");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(gesture), gesture, "No driver for this key.");
        }

        session.Pump();
        _output.WriteLine($"{gesture}: calls=[{string.Join(", ", session.Dialogs.Calls)}] nodes={vm.Editor.Nodes.Count} dirty={vm.IsDirty}");
    }

    [AvaloniaFact]
    public void SaveAs_ButtonAndShortcut_CallSaveFileOnce()
    {
        using var session = VisualSession.Open(_output);
        Button saveAs = session.ButtonNamed("Save As");
        string? tip = ToolTip.GetTip(saveAs) as string;
        _output.WriteLine($"tip='{tip}' bounds={session.BoundsInWindow(saveAs)}");
        tip.Should().Be("Save As (Ctrl+Shift+S)");

        session.Click(saveAs);
        session.RenderUntil(() => Count(session, SaveDialog) == 1, "the button asked for a file");
        session.Press("Ctrl+Shift+S");
        session.RenderUntil(() => Count(session, SaveDialog) == 2, "the key asked for a file");
        session.Pump();

        _output.WriteLine($"calls=[{string.Join(", ", session.Dialogs.Calls)}]");
        Count(session, SaveDialog).Should().Be(2, "the button and the key each ask once");
    }

    private static int Count(VisualSession session, string call) =>
        session.Dialogs.Calls.Count(name => string.Equals(name, call, StringComparison.Ordinal));

    // Opens the shortcuts dialog the way a user does, reads its rows and closes it again.
    private IReadOnlyList<(string Key, string Description)> ShownShortcuts(VisualSession session)
    {
        MainWindow window = session.Window;
        session.Press("F1");
        session.RenderUntil(
            () => window.OwnedWindows.OfType<ShortcutsWindow>().Any(dialog => dialog.IsVisible && dialog.Bounds.Width > 0),
            "shortcuts dialog shown");
        ShortcutsWindow dialog = window.OwnedWindows.OfType<ShortcutsWindow>().Single();
        try
        {
            IReadOnlyList<(string Key, string Description)> rows = ShortcutRows.Read(dialog);
            _output.WriteLine($"[shortcuts] rows={rows.Count}: {string.Join(" | ", rows.Select(row => $"{row.Key} = {row.Description}"))}");
            return rows;
        }
        finally
        {
            dialog.Close();
            session.RenderUntil(() => window.OwnedWindows.Count == 0, "shortcuts dialog closed");
        }
    }
}
