using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class ExecutionLogViewModelTests
{
    private readonly ExecutionLogViewModel _vm = new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance));

    [Fact]
    public void ReportProgressEvent_PhaseChanged_Enumerating_SetsIndeterminate()
    {
        _vm.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));

        _vm.IsIndeterminate.Should().BeTrue();
        _vm.Phase.Should().Be(ExecutionPhase.Enumerating);
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
        _vm.Phase.Should().Be(ExecutionPhase.Complete);
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
}
