using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.UI.ViewModels;
using FluentAssertions;

namespace FlowForge.Tests.ViewModels;

public class ExecutionLogViewModelTests
{
    private readonly ExecutionLogViewModel _vm = new();

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
    public void ReportProgress_CalculatesProgressPercentage()
    {
        _vm.TotalFiles = 4;
        var job1 = new FileJob { OriginalPath = "/a.txt", CurrentPath = "/a.txt", Status = FileJobStatus.Succeeded };
        var job2 = new FileJob { OriginalPath = "/b.txt", CurrentPath = "/b.txt", Status = FileJobStatus.Succeeded };

        _vm.ReportProgress(job1);
        _vm.Progress.Should().Be(25.0);

        _vm.ReportProgress(job2);
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
    }

    [Fact]
    public void ProgressText_ReturnsCorrectPluralization()
    {
        _vm.TotalFiles = 5;
        var job1 = new FileJob { OriginalPath = "/a.txt", CurrentPath = "/a.txt", Status = FileJobStatus.Succeeded };

        _vm.ReportProgress(job1);
        _vm.ProgressText.Should().Be("1 file");

        var job2 = new FileJob { OriginalPath = "/b.txt", CurrentPath = "/b.txt", Status = FileJobStatus.Failed };
        _vm.ReportProgress(job2);
        _vm.ProgressText.Should().Be("2 files");
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
}
