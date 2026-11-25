namespace WorkflowDelivery.Tests.Release;

internal static class PreparationCheckout
{
    private static readonly EnumerationOptions NoLinks = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    // Native preparation writes generated files into some checkouts. Copy the committed
    // seed, including its Git metadata, so those rows do not share writes or rerun Git setup.
    internal static string Copy(string seed, string root)
    {
        string checkout = Path.Combine(root, "checkout with spaces");
        CopyDirectory(seed, checkout);
        return checkout;
    }

    internal static void Delete(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (string file in Directory.EnumerateFiles(root, "*", NoLinks))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target);
            File.SetAttributes(target, FileAttributes.Normal);
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
