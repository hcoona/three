namespace WorkflowDelivery.Platform;

// Size facts of a file as it is actually read. A link's own FileInfo.Length is not its
// target's, so bounds and recorded sizes are taken from the opened stream.
internal static class OwnedFile
{
    internal static long Length(string path)
    {
        using FileStream file = File.OpenRead(path);
        return file.Length;
    }
}
