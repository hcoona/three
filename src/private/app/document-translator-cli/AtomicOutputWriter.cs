namespace Hcoona.DocumentTranslatorCli;

internal static class AtomicOutputWriter
{
    public static async ValueTask WriteAsync(
        string outputPath,
        BinaryData content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        string fullOutputPath = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        string tempPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
        bool moved = false;

        try
        {
            await using (FileStream tempStream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                using Stream contentStream = content.ToStream();
                await contentStream
                    .CopyToAsync(tempStream, cancellationToken)
                    .ConfigureAwait(false);
                await tempStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, fullOutputPath, overwrite);
            moved = true;
        }
        finally
        {
            if (!moved)
            {
                TryDelete(tempPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
