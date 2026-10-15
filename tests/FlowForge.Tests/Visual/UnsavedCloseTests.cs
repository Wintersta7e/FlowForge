using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.UI.Services;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// Closing the window with unsaved work asks first, once, and keeps the window open on Cancel;
/// the prompt names the pipeline and answers with the button the user clicked.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class UnsavedCloseTests
{
    private const string Prompt = nameof(IDialogService.ConfirmUnsavedChangesAsync);

    private readonly ITestOutputHelper _output;

    public UnsavedCloseTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AvaloniaFact]
    public void WindowClose_WhileDirty_PromptsOnceAndHonoursCancel()
    {
        using var session = VisualSession.Open(_output);
        Window window = session.Window;
        session.LoadTemplate(VisualSession.WebExportTemplate);
        session.ViewModel.IsDirty.Should().BeTrue("a template is unsaved work");

        session.Dialogs.ConfirmResult = UnsavedChangesChoice.Cancel;
        window.Close();
        session.RenderUntil(() => Prompts(session) == 1, "the close prompt answered");
        session.Pump();
        _output.WriteLine($"cancel: prompts={Prompts(session)} visible={window.IsVisible}");
        window.IsVisible.Should().BeTrue("Cancel keeps the window open");

        var answer = new TaskCompletionSource<UnsavedChangesChoice>();
        session.Dialogs.PendingConfirm = answer;
        window.Close();
        window.Close();
        session.Pump();
        _output.WriteLine($"second close while asking: prompts={Prompts(session)} visible={window.IsVisible}");
        Prompts(session).Should().Be(2, "a close while the prompt is open does not ask again");
        window.IsVisible.Should().BeTrue("nothing closes before the user answers");

        answer.SetResult(UnsavedChangesChoice.Discard);
        session.RenderUntil(() => !window.IsVisible, "the window closed after Discard");
        _output.WriteLine($"discard: prompts={Prompts(session)} visible={window.IsVisible}");
        Prompts(session).Should().Be(2);
    }

    [AvaloniaFact]
    public void Prompt_names_the_pipeline_and_returns_the_clicked_choice()
    {
        using var session = VisualSession.Open(_output);
        bool captured = false;
        foreach (UnsavedChangesChoice choice in Enum.GetValues<UnsavedChangesChoice>())
        {
            var prompt = new UnsavedChangesWindow { DataContext = "holiday.ffpipe" };
            Task<UnsavedChangesChoice?> result = prompt.ShowDialog<UnsavedChangesChoice?>(session.Window);
            session.RenderUntil(() => prompt.IsVisible && prompt.Bounds.Width > 0, "the prompt shown");
            var buttons = prompt.GetVisualDescendants().OfType<Button>().ToList();
            if (!captured)
            {
                using WriteableBitmap frame = session.Capture(VisualFrames.UnsavedChangesPrompt, prompt);
                captured = true;
            }

            _output.WriteLine(
                $"title='{prompt.Title}' text=[{string.Join(" | ", session.ShownText(prompt))}] " +
                $"buttons=[{string.Join(" | ", buttons.Select(b => $"{b.Content}{(b.Classes.Contains("accent") ? " (accent)" : string.Empty)}"))}]");
            prompt.Title.Should().Be("Unsaved changes");
            session.ShownText(prompt).Should().Contain("“holiday.ffpipe” has unsaved changes. Save them first?");
            buttons.Select(b => b.Content as string).Should().Equal("Save", "Discard", "Cancel");
            buttons.Where(b => b.Classes.Contains("accent")).Select(b => b.Content as string).Should().Equal("Save");

            session.Click(buttons.Single(b => string.Equals(b.Content as string, choice.ToString(), StringComparison.Ordinal)));
            session.RenderUntil(() => result.IsCompleted, $"{choice} answered");
            _output.WriteLine($"clicked {choice}: result={result.Result}");
            result.Result.Should().Be(choice);
        }

        var closed = new UnsavedChangesWindow { DataContext = "holiday.ffpipe" };
        Task<UnsavedChangesChoice?> dismissed = closed.ShowDialog<UnsavedChangesChoice?>(session.Window);
        session.RenderUntil(() => closed.IsVisible, "the prompt shown");
        closed.Close();
        session.RenderUntil(() => dismissed.IsCompleted, "the prompt closed by its frame");
        dismissed.Result.Should().BeNull("closing the prompt by its frame picks no button");
    }

    private static int Prompts(VisualSession session) => session.Dialogs.Calls.Count(call => string.Equals(call, Prompt, StringComparison.Ordinal));
}
