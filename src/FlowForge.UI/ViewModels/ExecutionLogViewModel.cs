using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;

namespace FlowForge.UI.ViewModels;

public partial class ExecutionLogViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
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
    [NotifyPropertyChangedFor(nameof(ProcessIdHex))]
    private bool _isRunning;

    // The editor's DEMO mode: the canvas animates, but no run touches any file.
    [ObservableProperty]
    private bool _isDemo;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isPanelExpanded = true;

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

    // Shown by any slot that has no reading to show.
    private const string NoReading = "\u2014";

    private static readonly string RunningProcessId = "0x" + Environment.ProcessId.ToString("x", CultureInfo.InvariantCulture);

    private readonly NodeRegistry _registry;
    private readonly Stopwatch _processingStopwatch = new();

    // Originals whose last live branch has ended: the one progress count, so it never passes TotalFiles.
    private int _completedOriginals;
    private bool _sourceCounted;
    private TimeSpan _elapsedAtLastTerminal;
    private StageProgress? _stage;

    public ExecutionLogViewModel(NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    public bool IsOutputTabSelected => SelectedTabIndex == 0;
    public bool IsErrorsTabSelected => SelectedTabIndex == 1;
    public bool IsWarningsTabSelected => SelectedTabIndex == 2;
    public string ProgressText => TotalFiles > 0 ? $"{_completedOriginals}/{TotalFiles}" : NoReading;

    public string ProcessIdHex => IsRunning ? RunningProcessId : NoReading;

    public string SourceCountText => _sourceCounted ? DiscoveredFiles.ToString(CultureInfo.InvariantCulture) : NoReading;

    // Mean processing time per finished original, for the current or last run.
    public string LatencyText => _completedOriginals == 0
        ? NoReading
        : (_elapsedAtLastTerminal.TotalMilliseconds / _completedOriginals).ToString("F1", CultureInfo.InvariantCulture) + "ms";

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
        if (!int.TryParse(indexStr, System.Globalization.CultureInfo.InvariantCulture, out int index))
        {
            return;
        }

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
        _completedOriginals = 0;
        _sourceCounted = false;
        _elapsedAtLastTerminal = TimeSpan.Zero;
        _stage = null;
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
        DiscoveredFiles = 0;
        PhaseLabel = string.Empty;
        IsIndeterminate = false;
        ThroughputRate = string.Empty;
        _processingStopwatch.Reset();
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(SourceCountText));
        OnPropertyChanged(nameof(LatencyText));
    }

    public void ReportProgressEvent(PipelineProgressEvent evt)
    {
        switch (evt)
        {
            case PhaseChanged phaseEvt:
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
                _sourceCounted = true;
                DiscoveredFiles = discovered.TotalCount;
                OnPropertyChanged(nameof(SourceCountText));
                PhaseLabel = $"Scanning... {discovered.TotalCount} files found";
                break;

            case StageProgress stage:
                _stage = stage;
                UpdatePhaseLabel();
                break;

            case FileProcessed processed:
                if (processed.CompletesOriginal)
                {
                    _completedOriginals++;
                }

                _elapsedAtLastTerminal = _processingStopwatch.Elapsed;
                RecordJob(processed.Job);
                break;
        }
    }

    private void RecordJob(FileJob job)
    {
        // Each status has its own list, so dropped or failed files never use up the Output list's cap.
        var entry = new FileJobLogEntryViewModel(job);
        switch (job.Status)
        {
            case FileJobStatus.Succeeded:
                Succeeded++;
                if (Entries.Count < MaxLogEntries)
                {
                    Entries.Add(entry);
                }

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

        CurrentFile = job.FileName;
        Progress = TotalFiles > 0 ? (double)_completedOriginals / TotalFiles * 100.0 : 0;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(LatencyText));
        UpdateThroughput();
        UpdatePhaseLabel();
    }

    private void UpdatePhaseLabel()
    {
        // While a transform step still has inputs to take, the label names that step.
        if (_stage is { } stage && stage.Done < stage.Total)
        {
            PhaseLabel = $"Stage {stage.Stage} of {stage.StageCount} \u00b7 {_registry.GetDisplayName(stage.TypeKey)} {stage.Done}/{stage.Total}";
            return;
        }

        string fileInfo = CurrentFile is not null ? $" \u2014 {CurrentFile}" : string.Empty;
        PhaseLabel = $"Processing {_completedOriginals} of {TotalFiles} files{fileInfo}";
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
