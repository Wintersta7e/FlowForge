using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public ObservableCollection<FileJobLogEntryViewModel> Entries { get; } = new();

    public void Clear()
    {
        Entries.Clear();
        TotalFiles = 0;
        Succeeded = 0;
        Failed = 0;
        Skipped = 0;
        Progress = 0;
        CurrentFile = null;
        IsRunning = false;
        Summary = null;
    }

    public void ReportProgress(FileJob job)
    {
        Entries.Add(new FileJobLogEntryViewModel(job));
        CurrentFile = job.FileName;

        switch (job.Status)
        {
            case FileJobStatus.Succeeded:
                Succeeded++;
                break;
            case FileJobStatus.Failed:
                Failed++;
                break;
            case FileJobStatus.Skipped:
                Skipped++;
                break;
        }

        int processed = Succeeded + Failed + Skipped;
        Progress = TotalFiles > 0 ? (double)processed / TotalFiles * 100.0 : 0;
    }
}
