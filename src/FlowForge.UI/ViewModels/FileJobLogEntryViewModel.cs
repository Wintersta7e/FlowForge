using System.Collections.Generic;
using FlowForge.Core.Models;

namespace FlowForge.UI.ViewModels;

public class FileJobLogEntryViewModel : ViewModelBase
{
    public string FileName { get; }
    public FileJobStatus Status { get; }
    public string StatusText { get; }
    public string? ErrorMessage { get; }
    public List<string> NodeLog { get; }
    public bool IsError { get; }

    public FileJobLogEntryViewModel(FileJob job)
    {
        FileName = job.FileName;
        Status = job.Status;
        ErrorMessage = job.ErrorMessage;
        NodeLog = job.NodeLog;
        IsError = job.Status == FileJobStatus.Failed;

        StatusText = job.Status switch
        {
            FileJobStatus.Succeeded => "OK",
            FileJobStatus.Failed => "FAIL",
            FileJobStatus.Skipped => "SKIP",
            _ => job.Status.ToString().ToUpperInvariant()
        };
    }
}
