using System.Text.Json;
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

    // Bind a checkout whose committed tree the caller already read, e.g. from a comparison.
    internal static async Task<GitMaterialization> BindAsync(string directory,
        GitRevision revision, CancellationToken token)
    {
        (string root, _) = await BindCheckoutAsync(directory, revision.Commit, token);
        return new(root, revision);
    }

    // Bind the checkout of a reference and list its committed tree once. Callers that require
    // an exact commit identity reject aliases before calling this.
    internal static async Task<GitMaterialization> BindAsync(string directory, string reference,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        (string root, string head) = await BindCheckoutAsync(directory, reference, token);
        return new(root, await new GitReader(root).ReadCommitAsync(head, token));
    }

    // Reuse a binding made earlier in this invocation for the same checkout and commit.
    internal static async Task<GitMaterialization> ReuseOrBindAsync(GitMaterialization? bound,
        string directory, string commit, CancellationToken token)
    {
        if (bound is null)
            return await BindAsync(directory, commit, token);
        if (bound.Revision.Commit != commit || !bound.Root.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
                OwnedPath.Comparison))
            throw new InvalidDataException("A reused binding names a different checkout.");
        // Earlier native work in this invocation may have changed tracked files.
        await RequireCleanAsync(bound.Root, token);
        return bound;
    }

    // Bind whatever commit the checkout has, for callers whose subject is the checkout itself.
    internal static async Task<GitMaterialization> BindHeadAsync(string directory,
        CancellationToken token)
    {
        (string root, string head) = await BindCheckoutAsync(directory, null, token);
        return new(root, await new GitReader(root).ReadCommitAsync(head, token));
    }

    // The checkout is runner/caller state, but these remain application binding checks:
    // relative committed paths are resolved under the root, so it must be the physical
    // checkout root; HEAD must be the bound commit; and tracked files must be unmodified
    // because native tools evaluate the working tree, not the Git objects.
    private static async Task<(string Root, string Head)> BindCheckoutAsync(string directory,
        string? reference, CancellationToken token)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        // One native query answers both the checkout root and the HEAD commit.
        string output = (await NativeProcess.RunAsync("git", root,
            ["--no-lazy-fetch", "--no-replace-objects", "rev-parse", "--show-toplevel",
                "--verify", "--end-of-options", "HEAD^{commit}"], token)).TrimEnd('\r', '\n');
        int separator = output.LastIndexOf('\n');
        string head = output[(separator + 1)..].TrimEnd('\r');
        if (separator <= 0 || !GitReader.IsCommitId(head))
            throw new InvalidDataException("Git returned an invalid checkout binding.");
        string nativeRoot = output[..separator].TrimEnd('\r');
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeRoot)), root,
                OwnedPath.Comparison))
            throw new InvalidDataException(
                "A materialization must be its physical (unlinked) checkout root.");
        // A full object id must be HEAD's own commit id: a tag object id naming HEAD is not an
        // exact commit identity. Other references are resolved. HEAD is compared before the
        // status query, so a wrong checkout reports its revision first.
        if (reference is not null && (GitReader.IsCommitId(reference) ? reference != head :
                await new GitReader(root).ResolveAsync(reference, token) != head))
            throw new InvalidDataException("The checkout HEAD is not the requested revision.");
        await RequireCleanAsync(root, token);
        return (root, head);
    }

    private static async Task RequireCleanAsync(string root, CancellationToken token)
    {
        string status = await NativeProcess.RunAsync("git", root,
            ["--no-lazy-fetch", "--no-replace-objects", "status", "--porcelain=v1", "-z",
                "--untracked-files=no", "--ignore-submodules=none"], token);
        if (status.Length != 0)
            throw new InvalidDataException("Planning requires a clean tracked checkout. " +
                $"Root: \"{JsonEncodedText.Encode(root)}\"; " +
                $"Git status: \"{JsonEncodedText.Encode(status)}\".");
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
