using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    }

    public void ReportProgress(FileJob job)
    {
        var entry = new FileJobLogEntryViewModel(job);
        Entries.Add(entry);
        CurrentFile = job.FileName;

        switch (job.Status)
        {
            case FileJobStatus.Succeeded:
                Succeeded++;
                break;
            case FileJobStatus.Failed:
                Failed++;
                ErrorEntries.Add(entry);
                break;
            case FileJobStatus.Skipped:
                Skipped++;
                WarningEntries.Add(entry);
                break;
        }

        int processed = Succeeded + Failed + Skipped;
        Progress = TotalFiles > 0 ? (double)processed / TotalFiles * 100.0 : 0;
        OnPropertyChanged(nameof(ProgressText));
    }
}
