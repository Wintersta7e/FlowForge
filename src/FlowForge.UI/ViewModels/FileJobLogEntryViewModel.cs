using System;
using System.Collections.Generic;
using System.Globalization;
using FlowForge.Core.Models;

namespace FlowForge.UI.ViewModels;

public class FileJobLogEntryViewModel : ViewModelBase
{
    public string FileName { get; }
    public FileJobStatus Status { get; }
    public string StatusText { get; }
    public string? ErrorMessage { get; }
    public List<string> NodeLog { get; }
    public bool IsSuccess { get; }
    public bool IsError { get; }
    public bool IsWarning { get; }
    public string Timestamp { get; }

    public FileJobLogEntryViewModel(FileJob job)
    {
        FileName = job.FileName;
        Status = job.Status;
        ErrorMessage = job.ErrorMessage;
        NodeLog = job.NodeLog;
        IsSuccess = job.Status == FileJobStatus.Succeeded;
        IsError = job.Status == FileJobStatus.Failed;
        IsWarning = job.Status == FileJobStatus.Skipped;
        Timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        StatusText = job.Status switch
        {
            FileJobStatus.Succeeded => "OK",
            FileJobStatus.Failed => "FAIL",
            FileJobStatus.Skipped => "SKIP",
            _ => job.Status.ToString().ToUpperInvariant()
        };
    }
}
