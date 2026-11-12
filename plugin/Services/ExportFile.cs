using KeySender.Models;

namespace KeySender.Services;

internal static class ExportFile
{
    internal static async Task<TransferOperationResult> WriteAsync(
        TransferService transfer,
        IReadOnlyList<string> nodeIds,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destination = Path.GetFullPath(destinationPath);
        var stagingPath = Path.Combine(Path.GetDirectoryName(destination)!, $".keysender-{Guid.NewGuid():N}.tmp");
        var stream = new FileStream(
            stagingPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        var published = false;
        try
        {
            TransferOperationResult result;
            await using (stream.ConfigureAwait(false))
            {
                result = await transfer.ExportAsync(nodeIds, stream, cancellationToken).ConfigureAwait(false);
                if (!result.Ok)
                {
                    return result;
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // The staged file is closed and in the same directory so replacement is one rename.
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagingPath, destination, overwrite: true);
            published = true;
            return result;
        }
        finally
        {
            if (!published)
            {
                try
                {
                    File.Delete(stagingPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Keep the original export result or exception if cleanup also fails.
                }
            }
        }
    }
}
