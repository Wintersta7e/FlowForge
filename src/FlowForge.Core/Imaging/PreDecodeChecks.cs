using FlowForge.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Imaging;

/// <summary>
/// The checks an image node runs before it reads any content, in live and dry runs alike, so a dry run reports
/// the refusal a live run would. Each one reads only configuration or a file's size, and returns true after
/// failing the job with an error that starts with the node's type key.
/// </summary>
internal static class PreDecodeChecks
{
    internal const long MaxFileSizeBytes = 500L * 1024 * 1024;

    /// <summary>Fails the job when <paramref name="targetFormat"/>, the format the node would write, is null.</summary>
    internal static bool FailIfTargetUnresolved(FileJob job, string typeKey, string? targetFormat, string detail, ILogger logger)
    {
        if (targetFormat is not null)
        {
            return false;
        }

        Fail(job, typeKey, detail, logger);
        return true;
    }

    /// <summary>Fails the job when the file it reads is larger than <see cref="MaxFileSizeBytes"/>.</summary>
    internal static bool FailIfTooLarge(FileJob job, string typeKey, ILogger logger)
    {
        long length = new FileInfo(job.ContentPath).Length;
        if (length <= MaxFileSizeBytes)
        {
            return false;
        }

        Fail(job, typeKey, $"File too large ({length / (1024 * 1024)} MB, max 500 MB).", logger);
        return true;
    }

    private static void Fail(FileJob job, string typeKey, string detail, ILogger logger)
    {
        job.Status = FileJobStatus.Failed;
        job.ErrorMessage = $"{typeKey}: {detail}";
        job.NodeLog.Add(job.ErrorMessage);
        logger.LogWarning("{NodeType} refused {File} before decoding: {Reason}", typeKey, job.OriginalPath, job.ErrorMessage);
    }
}
