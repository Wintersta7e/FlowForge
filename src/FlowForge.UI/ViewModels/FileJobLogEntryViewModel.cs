using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlowForge.Core.Models;

namespace FlowForge.UI.ViewModels;

public class FileJobLogEntryViewModel : ViewModelBase
{
    public string FileName { get; }
    public FileJobStatus Status { get; }
    public string StatusText { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<string> NodeLog { get; }
    public string NodeLogText => string.Join(Environment.NewLine, NodeLog);
    public string Timestamp { get; }

    public FileJobLogEntryViewModel(FileJob job)
    {
        FileName = job.FileName;
        Status = job.Status;
        ErrorMessage = job.ErrorMessage;
        NodeLog = job.NodeLog as IReadOnlyList<string> ?? job.NodeLog.ToList();
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
