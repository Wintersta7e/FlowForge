using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;

namespace FlowForge.UI.ViewModels;

public partial class ExecutionLogViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _succeeded;

    [ObservableProperty]
    private int _failed;

    [ObservableProperty]
    private int _skipped;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string? _currentFile;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isPanelExpanded = true;

    [ObservableProperty]
    private ExecutionPhase _phase;

    [ObservableProperty]
    private int _discoveredFiles;

    [ObservableProperty]
    private string _phaseLabel = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string _throughputRate = string.Empty;

    private const double MinElapsedSecondsForThroughput = 0.1;
    private const int MaxLogEntries = 5000;
    private readonly Stopwatch _processingStopwatch = new();

    public bool IsOutputTabSelected => SelectedTabIndex == 0;
    public bool IsErrorsTabSelected => SelectedTabIndex == 1;
    public bool IsWarningsTabSelected => SelectedTabIndex == 2;
    public string ProgressText
    {
        get
        {
            int processed = Succeeded + Failed + Skipped;
            return processed == 1 ? "1 file" : $"{processed} files";
        }
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOutputTabSelected));
        OnPropertyChanged(nameof(IsErrorsTabSelected));
        OnPropertyChanged(nameof(IsWarningsTabSelected));
    }

    public ObservableCollection<FileJobLogEntryViewModel> Entries { get; } = new();
    public ObservableCollection<FileJobLogEntryViewModel> ErrorEntries { get; } = new();
    public ObservableCollection<FileJobLogEntryViewModel> WarningEntries { get; } = new();

    [RelayCommand]
    private void SelectTab(string indexStr)
    {
        if (!int.TryParse(indexStr, out int index))
            return;

        if (SelectedTabIndex == index && IsPanelExpanded)
        {
            IsPanelExpanded = false;
        }
        else
        {
            SelectedTabIndex = index;
            IsPanelExpanded = true;
        }
    }

    public void Clear()
    {
        Entries.Clear();
        ErrorEntries.Clear();
        WarningEntries.Clear();
        TotalFiles = 0;
        Succeeded = 0;
        Failed = 0;
        Skipped = 0;
        Progress = 0;
        CurrentFile = null;
        IsRunning = false;
        Summary = null;
        Phase = default;
        DiscoveredFiles = 0;
        PhaseLabel = string.Empty;
        IsIndeterminate = false;
        ThroughputRate = string.Empty;
        _processingStopwatch.Reset();
    }

    public void ReportProgressEvent(PipelineProgressEvent evt)
    {
        switch (evt)
        {
            case PhaseChanged phaseEvt:
                Phase = phaseEvt.Phase;
                switch (phaseEvt.Phase)
                {
                    case ExecutionPhase.Enumerating:
                        IsIndeterminate = true;
                        PhaseLabel = "Scanning...";
                        break;
                    case ExecutionPhase.Processing:
                        IsIndeterminate = false;
                        TotalFiles = DiscoveredFiles;
                        _processingStopwatch.Restart();
                        UpdatePhaseLabel();
                        break;
                    case ExecutionPhase.Complete:
                        _processingStopwatch.Stop();
                        IsIndeterminate = false;
                        Progress = 100.0;
                        UpdateCompletionLabel();
                        break;
                }

                break;

            case FilesDiscovered discovered:
                DiscoveredFiles = discovered.TotalCount;
                PhaseLabel = $"Scanning... {discovered.TotalCount} files found";
                break;

            case FileProcessed processed:
                ReportProgress(processed.Job);
                UpdateThroughput();
                UpdatePhaseLabel();
                break;
        }
    }

    public void ReportProgress(FileJob job)
    {
        var entry = new FileJobLogEntryViewModel(job);

        if (Entries.Count < MaxLogEntries)
        {
            Entries.Add(entry);
        }

        CurrentFile = job.FileName;

        switch (job.Status)
        {
            case FileJobStatus.Succeeded:
                Succeeded++;
                break;
            case FileJobStatus.Failed:
                Failed++;
                if (ErrorEntries.Count < MaxLogEntries)
                {
                    ErrorEntries.Add(entry);
                }

                break;
            case FileJobStatus.Skipped:
                Skipped++;
                if (WarningEntries.Count < MaxLogEntries)
                {
                    WarningEntries.Add(entry);
                }

                break;
        }

        int processed = Succeeded + Failed + Skipped;
        Progress = TotalFiles > 0 ? (double)processed / TotalFiles * 100.0 : 0;
        OnPropertyChanged(nameof(ProgressText));
    }

    private void UpdatePhaseLabel()
    {
        int processed = Succeeded + Failed + Skipped;
        string fileInfo = CurrentFile is not null ? $" \u2014 {CurrentFile}" : string.Empty;
        PhaseLabel = $"Processing {processed} of {TotalFiles} files{fileInfo}";
    }

    private void UpdateCompletionLabel()
    {
        string rate = ThroughputRate.Length > 0 ? $" \u2014 {ThroughputRate}" : string.Empty;
        PhaseLabel = $"Complete: {Succeeded} OK, {Failed} failed, {Skipped} skipped{rate}";
    }

    private void UpdateThroughput()
    {
        int processed = Succeeded + Failed + Skipped;
        double elapsed = _processingStopwatch.Elapsed.TotalSeconds;
        if (elapsed > MinElapsedSecondsForThroughput)
        {
            double rate = processed / elapsed;
            ThroughputRate = $"{rate:F1} files/sec";
        }
    }
}
