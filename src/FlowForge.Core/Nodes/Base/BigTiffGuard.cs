using FlowForge.Core.Models;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Base;

internal static class BigTiffGuard
{
    /// <summary>
    /// Fails <paramref name="job"/> and returns <see langword="true"/> when its file starts with a BigTIFF header.
    /// </summary>
    public static async Task<bool> RejectIfBigTiffAsync(FileJob job, string typeKey, ILogger logger, CancellationToken ct)
    {
        // The decoder loops on a crafted BigTIFF header and ignores cancellation, so the header is checked before any decode.
        byte[] header = new byte[4];
        int read;
        FileStream stream = new(job.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            read = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        }

        if (!IsBigTiffMagic(header.AsSpan(0, read)))
        {
            return false;
        }

        logger.LogWarning("{TypeKey}: refusing BigTIFF input {FilePath}", typeKey, job.CurrentPath);
        job.Status = FileJobStatus.Failed;
        job.ErrorMessage = $"{typeKey}: BigTIFF input is not supported.";
        job.NodeLog.Add(job.ErrorMessage);
        return true;
    }

    private static bool IsBigTiffMagic(ReadOnlySpan<byte> header)
    {
        return header.SequenceEqual((ReadOnlySpan<byte>)[0x49, 0x49, 0x2B, 0x00])
            || header.SequenceEqual((ReadOnlySpan<byte>)[0x4D, 0x4D, 0x00, 0x2B]);
    }
}
