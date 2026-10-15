using System.Globalization;
using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class ExecutionLogViewModelTests
{
    private readonly ExecutionLogViewModel _vm = new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance));
    private readonly ITestOutputHelper _output;

    public ExecutionLogViewModelTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void ReportProgressEvent_PhaseChanged_Enumerating_SetsIndeterminate()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));

        _vm.IsIndeterminate.Should().BeTrue();
        _vm.PhaseLabel.Should().Be("Scanning...");
    }

    [Fact]
    public void ReportProgressEvent_FilesDiscovered_UpdatesCount()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(5));

        _vm.DiscoveredFiles.Should().Be(5);
        _vm.PhaseLabel.Should().Contain("5 files found");
    }

    [Fact]
    public void ReportProgressEvent_PhaseChanged_Processing_SetsTotalFiles()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(10));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        _vm.IsIndeterminate.Should().BeFalse();
        _vm.TotalFiles.Should().Be(10);
    }

    [Fact]
    public void ReportProgressEvent_FileProcessed_IncrementsSucceeded()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(2));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        var job = new FileJob { OriginalPath = "/path/test.txt", CurrentPath = "/path/test.txt", Status = FileJobStatus.Succeeded };
        _vm.ReportProgressEvent(new FileProcessed(job));

        _vm.Succeeded.Should().Be(1);
        _vm.Entries.Should().HaveCount(1);
        _vm.CurrentFile.Should().Be("test.txt");
    }

    [Fact]
    public void ReportProgressEvent_FileProcessed_Failed_AddsToErrorEntries()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        var job = new FileJob { OriginalPath = "/path/fail.txt", CurrentPath = "/path/fail.txt", Status = FileJobStatus.Failed };
        _vm.ReportProgressEvent(new FileProcessed(job));

        _vm.Failed.Should().Be(1);
        _vm.ErrorEntries.Should().HaveCount(1);
    }

    [Fact]
    public void ReportProgressEvent_FileProcessed_Skipped_AddsToWarningEntries()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        var job = new FileJob { OriginalPath = "/path/skip.txt", CurrentPath = "/path/skip.txt", Status = FileJobStatus.Skipped };
        _vm.ReportProgressEvent(new FileProcessed(job));

        _vm.Skipped.Should().Be(1);
        _vm.WarningEntries.Should().HaveCount(1);
    }

    [Fact]
    public void ReportProgressEvent_PhaseChanged_Complete_SetsProgress100()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Complete));

        _vm.Progress.Should().Be(100.0);
        _vm.IsIndeterminate.Should().BeFalse();
        _vm.PhaseLabel.Should().StartWith("Complete:");
    }

    [Fact]
    public void Progress_counts_finished_originals_only()
    {
        _vm.TotalFiles = 4;
        var job1 = new FileJob { OriginalPath = "/a.txt", CurrentPath = "/a.txt", Status = FileJobStatus.Succeeded };
        var job2 = new FileJob { OriginalPath = "/b.txt", CurrentPath = "/b.txt", Status = FileJobStatus.Succeeded };

        _vm.ReportProgressEvent(new FileProcessed(job1));
        _vm.Progress.Should().Be(25.0);

        // One branch of an original ends while another branch of it is still live.
        _vm.ReportProgressEvent(new FileProcessed(job2) { CompletesOriginal = false });
        _vm.Progress.Should().Be(25.0);

        _vm.ReportProgressEvent(new FileProcessed(job2));
        _vm.Progress.Should().Be(50.0);
    }

    [Fact]
    public void Clear_ResetsAllProperties()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(5));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));
        var job = new FileJob { OriginalPath = "/x.txt", CurrentPath = "/x.txt", Status = FileJobStatus.Succeeded };
        _vm.ReportProgressEvent(new FileProcessed(job));
        _vm.SourceCountText.Should().Be("5");
        _vm.ProgressText.Should().Be("1/5");
        _vm.LatencyText.Should().EndWith("ms");

        _vm.Clear();

        _vm.TotalFiles.Should().Be(0);
        _vm.Succeeded.Should().Be(0);
        _vm.Failed.Should().Be(0);
        _vm.Skipped.Should().Be(0);
        _vm.Progress.Should().Be(0);
        _vm.CurrentFile.Should().BeNull();
        _vm.IsRunning.Should().BeFalse();
        _vm.Summary.Should().BeNull();
        _vm.Entries.Should().BeEmpty();
        _vm.ErrorEntries.Should().BeEmpty();
        _vm.WarningEntries.Should().BeEmpty();
        _vm.DiscoveredFiles.Should().Be(0);
        _vm.PhaseLabel.Should().BeEmpty();
        _vm.IsIndeterminate.Should().BeFalse();
        _vm.ThroughputRate.Should().BeEmpty();
        _vm.ProgressText.Should().Be("—");
        _vm.SourceCountText.Should().Be("—");
        _vm.LatencyText.Should().Be("—");
        _vm.ProcessIdHex.Should().Be("—");
    }

    [Fact]
    public void ProgressText_reads_finished_originals_over_total()
    {
        _vm.ProgressText.Should().Be("—", "no total is known before a run is processing");
        _vm.TotalFiles = 5;
        _vm.ProgressText.Should().Be("0/5");
        var job1 = new FileJob { OriginalPath = "/a.txt", CurrentPath = "/a.txt", Status = FileJobStatus.Succeeded };

        _vm.ReportProgressEvent(new FileProcessed(job1));
        _vm.ProgressText.Should().Be("1/5");

        var job2 = new FileJob { OriginalPath = "/b.txt", CurrentPath = "/b.txt", Status = FileJobStatus.Failed };
        _vm.ReportProgressEvent(new FileProcessed(job2) { CompletesOriginal = false });
        _vm.ProgressText.Should().Be("1/5");

        _vm.ReportProgressEvent(new FileProcessed(job2));
        _vm.ProgressText.Should().Be("2/5");
    }

    [Fact]
    public void StageProgress_names_the_step_and_leaves_progress_on_finished_files()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(1000));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        _vm.ReportProgressEvent(new StageProgress("ImageResize", 2, 4, 312, 1000));
        _vm.PhaseLabel.Should().Be("Stage 2 of 4 \u00b7 Image Resize 312/1000");
        _vm.Progress.Should().Be(0, "no file has finished");
        _vm.ProgressText.Should().Be("0/1000");

        // A file the step drops is finished; the label still names the step.
        var dropped = new FileJob { OriginalPath = "/a.txt", CurrentPath = "/a.txt", Status = FileJobStatus.Skipped };
        _vm.ReportProgressEvent(new FileProcessed(dropped));
        _vm.PhaseLabel.Should().Be("Stage 2 of 4 \u00b7 Image Resize 312/1000");
        _vm.Progress.Should().BeApproximately(0.1, 1e-9);

        // Once the step has taken every input, the label counts finished files again.
        _vm.ReportProgressEvent(new StageProgress("ImageResize", 2, 4, 1000, 1000));
        _vm.PhaseLabel.Should().StartWith("Processing ");
    }

    [Fact]
    public void SelectTab_TogglesExpansion_WhenSameTabSelected()
    {
        _vm.SelectedTabIndex = 0;
        _vm.IsPanelExpanded = true;

        _vm.SelectTabCommand.Execute("0");
        _vm.IsPanelExpanded.Should().BeFalse();
    }

    [Fact]
    public void SelectTab_SwitchesToTab_WhenDifferentTabSelected()
    {
        _vm.SelectedTabIndex = 0;
        _vm.IsPanelExpanded = true;

        _vm.SelectTabCommand.Execute("1");
        _vm.SelectedTabIndex.Should().Be(1);
        _vm.IsPanelExpanded.Should().BeTrue();
    }

    [Fact]
    public void Entries_cap_at_5000_while_counters_keep_counting()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(5001));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        for (int i = 0; i < 5001; i++)
        {
            string path = Path.Combine(Path.GetTempPath(), $"f{i}.txt");
            var job = new FileJob { OriginalPath = path, CurrentPath = path, Status = FileJobStatus.Succeeded };
            _vm.ReportProgressEvent(new FileProcessed(job));
        }

        _vm.Entries.Should().HaveCount(5000);
        _vm.Succeeded.Should().Be(5001);
    }

    [Fact]
    public void Output_lists_succeeded_files_even_after_5000_dropped_files()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(5001));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

        for (int i = 0; i < 5000; i++)
        {
            string path = Path.Combine(Path.GetTempPath(), $"dropped{i}.txt");
            _vm.ReportProgressEvent(new FileProcessed(new FileJob { OriginalPath = path, CurrentPath = path, Status = FileJobStatus.Skipped }));
        }

        string kept = Path.Combine(Path.GetTempPath(), "kept.txt");
        _vm.ReportProgressEvent(new FileProcessed(new FileJob { OriginalPath = kept, CurrentPath = kept, Status = FileJobStatus.Succeeded }));

        _output.WriteLine($"output rows={_vm.Entries.Count} statuses={string.Join(",", _vm.Entries.Select(e => e.StatusText).Distinct(StringComparer.Ordinal))} skipped rows={_vm.WarningEntries.Count}");
        _vm.Entries.Select(e => e.FileName).Should().Equal(["kept.txt"], "the Output tab lists the files that succeeded");
        _vm.WarningEntries.Should().HaveCount(5000, "the dropped files are listed on the Skipped tab");
    }

    [Fact]
    public void A_run_error_lists_each_message_line_on_the_errors_tab()
    {
        string message = string.Join(Environment.NewLine, "Pipeline graph contains a cycle.", "Unknown node type: 'Missing'");

        _vm.RecordRunError(message);

        _output.WriteLine($"error rows={string.Join(" | ", _vm.ErrorEntries.Select(e => $"{e.StatusText} '{e.FileName}' {e.ErrorMessage}"))} failed={_vm.Failed}");
        _vm.ErrorEntries.Select(e => e.ErrorMessage).Should().Equal(["Pipeline graph contains a cycle.", "Unknown node type: 'Missing'"]);
        _vm.ErrorEntries.Should().OnlyContain(e => e.FileName.Length == 0, "the rows name no file");
        _vm.Failed.Should().Be(0, "no file failed");
    }

    [Fact]
    public void FanOutEvents_NeverExceedTotal()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        _vm.ReportProgressEvent(new FilesDiscovered(3));
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));
        int maxNumerator = 0;
        double maxProgress = 0;
        int maxShownCount = 0;

        // Each original splits into two branches; only the branch that ends last completes it.
        foreach (string name in new[] { "alpha.txt", "beta.txt", "gamma.txt" })
        {
            foreach (bool completes in new[] { false, true })
            {
                string path = Path.Combine(Path.GetTempPath(), name);
                var job = new FileJob { OriginalPath = path, CurrentPath = path, Status = FileJobStatus.Succeeded };
                _vm.ReportProgressEvent(new FileProcessed(job) { CompletesOriginal = completes });

                int numerator = int.Parse(_vm.ProgressText.Split('/')[0], CultureInfo.InvariantCulture);
                int shownCount = Counts(_vm.ProgressText).Concat(Counts(_vm.PhaseLabel)).Max();
                _output.WriteLine($"{name} completes={completes}: count={_vm.ProgressText} progress={_vm.Progress} label='{_vm.PhaseLabel}'");
                maxNumerator = Math.Max(maxNumerator, numerator);
                maxProgress = Math.Max(maxProgress, _vm.Progress);
                maxShownCount = Math.Max(maxShownCount, shownCount);
            }
        }

        _output.WriteLine($"max numerator={maxNumerator} max progress={maxProgress} max count shown={maxShownCount}");
        maxNumerator.Should().BeLessThanOrEqualTo(3, "only the three originals count toward progress");
        maxProgress.Should().BeLessThanOrEqualTo(100);
        maxShownCount.Should().BeLessThanOrEqualTo(3, "neither the count nor the label counts a branch as a file");
    }

    private static IEnumerable<int> Counts(string text) =>
        Regex.Matches(text, @"\d+", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture));
}
