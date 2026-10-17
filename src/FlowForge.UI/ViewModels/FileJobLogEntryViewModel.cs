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
        : this(job.FileName, job.Status, job.ErrorMessage, job.NodeLog as IReadOnlyList<string> ?? job.NodeLog.ToList())
    {
    }

    private FileJobLogEntryViewModel(string fileName, FileJobStatus status, string? errorMessage, IReadOnlyList<string> nodeLog)
    {
        FileName = fileName;
        Status = status;
        ErrorMessage = errorMessage;
        NodeLog = nodeLog;
        Timestamp = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

        StatusText = status switch
        {
            FileJobStatus.Succeeded => "OK",
            FileJobStatus.Failed => "FAIL",
            FileJobStatus.Skipped => "SKIP",
            _ => status.ToString().ToUpperInvariant()
        };
    }

    // A failure of the run itself rather than of one file: no file name, the message as its error.
    public static FileJobLogEntryViewModel ForRunError(string message) =>
        new(string.Empty, FileJobStatus.Failed, message, Array.Empty<string>());
}
