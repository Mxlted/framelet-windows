namespace Framelet.Core;

public enum RecordingFileState { Incomplete, Discarded, Finalized, Published }

/// <summary>The encoder completed successfully, but its private file could not be published.</summary>
public sealed class RecordingPublicationException : IOException
{
    public string RecoveryPath { get; }

    public RecordingPublicationException(string recoveryPath, Exception? inner = null)
        : base($"The recording is complete, but its destination could not be saved. The completed MP4 is preserved at {recoveryPath}. Choose Save recording as to save it elsewhere.", inner)
        => RecoveryPath = Path.GetFullPath(recoveryPath);
}

public static class RecordingRecovery
{
    /// <summary>Publish a copy atomically without replacing an existing file. Keep the recovery
    /// source even after success, so a failed cleanup cannot endanger the only completed copy.</summary>
    public static async Task SaveAsAsync(string recoveryPath, string destination, CancellationToken cancellation = default)
    {
        recoveryPath = Path.GetFullPath(recoveryPath);
        destination = Path.GetFullPath(destination);
        if (!Path.GetExtension(destination).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Save the completed recording as an MP4 file.", nameof(destination));
        if (File.Exists(destination)) throw new IOException("The destination already exists. Choose a new filename.");
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, $".framelet-recovery-{Guid.NewGuid():N}.mp4");
        try
        {
            await using (var source = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(output, cancellation).ConfigureAwait(false);
                await output.FlushAsync(cancellation).ConfigureAwait(false);
                output.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(staging, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }
}
