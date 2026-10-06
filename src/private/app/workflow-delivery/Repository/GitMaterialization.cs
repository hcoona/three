using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

// The caller/platform owns checkout creation and lifetime. Ordinary Git checks bind it;
// no checkout manager, historical path audit or content-proof record is introduced.
internal sealed class GitMaterialization
{
    internal string Root { get; }
    internal GitRevision Revision { get; }
    private readonly Dictionary<string, GitEntry> entries;

    private GitMaterialization(string root, GitRevision revision)
    {
        Root = root;
        Revision = revision;
        entries = revision.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
    }

    internal static async Task<GitMaterialization> BindAsync(string directory,
        GitRevision revision, CancellationToken token)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string nativeRoot = await NativeProcess.RunAsync("git", root,
            ["--no-lazy-fetch", "--no-replace-objects", "rev-parse", "--show-toplevel"], token);
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                nativeRoot.TrimEnd('\r', '\n'))), root, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("A materialization must be its checkout root.");
        GitRevision head = await new GitReader(root).ReadAsync("HEAD", token);
        if (head.Commit != revision.Commit)
            throw new InvalidDataException("The checkout HEAD is not the requested revision.");
        string status = await NativeProcess.RunAsync("git", root,
            ["--no-lazy-fetch", "--no-replace-objects", "status", "--porcelain=v1", "-z",
                "--untracked-files=no", "--ignore-submodules=none"], token);
        if (status.Length != 0)
            throw new InvalidDataException("Planning requires a clean tracked checkout.");
        return new(root, revision);
    }

    internal async Task<string?> ReadOptionalTextAsync(string path, CancellationToken token)
    {
        if (!entries.TryGetValue(path, out GitEntry? entry))
            return null;
        if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException(
                $"Required declaration is not a regular Git file: {path}");
        return await File.ReadAllTextAsync(Path.Combine(Root, path), token);
    }
}
