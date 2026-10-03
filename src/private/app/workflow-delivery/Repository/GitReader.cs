using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record GitEntry(string Path, string Mode, string ObjectType);
internal sealed record GitRevision(string Commit, GitEntry[] Entries);
internal sealed record GitComparison(GitRevision Basis, GitRevision Candidate,
    string[] ChangedPaths);

// Git owns revision, tree and diff semantics; the caller chooses both endpoints.
internal sealed class GitReader(string directory, string executable = "git")
{
    private readonly string directory = Path.GetFullPath(directory);

    internal async Task<GitRevision> ReadAsync(string reference, CancellationToken token)
    {
        string commit = await ResolveAsync(reference, token);
        return await ReadCommitAsync(commit, token);
    }

    internal async Task<GitComparison> CompareAsync(string basis, string candidate,
        CancellationToken token)
    {
        string basisCommit = await ResolveAsync(basis, token);
        string candidateCommit = await ResolveAsync(candidate, token);
        GitRevision before = await ReadCommitAsync(basisCommit, token);
        GitRevision after = await ReadCommitAsync(candidateCommit, token);
        string changes = await QueryAsync(["diff-tree", "-r", "--no-commit-id",
            "--name-only", "-z", "--no-renames", "--no-relative", "--no-ext-diff",
            "--no-textconv", "--ignore-submodules=none", basisCommit, candidateCommit,
            "--"], token);
        return new(before, after, Records(changes).Order(StringComparer.Ordinal).ToArray());
    }

    private async Task<string> ResolveAsync(string reference, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        string output = await QueryAsync(["rev-parse", "--verify", "--end-of-options",
            reference + "^{commit}"], token);
        string commit = output.TrimEnd('\r', '\n');
        if (commit.Length == 0 || commit.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Git returned an invalid commit identity.");
        return commit;
    }

    private async Task<GitRevision> ReadCommitAsync(string commit, CancellationToken token)
    {
        string output = await QueryAsync(["ls-tree", "-r", "-z", "--full-tree",
            commit, "--"], token);
        return new(commit, Records(output).Select(record =>
        {
            int separator = record.IndexOf('\t');
            if (separator < 0 || separator == record.Length - 1)
                throw new InvalidDataException("Git returned an invalid tree entry.");
            string[] metadata = record[..separator].Split(' ');
            if (metadata.Length != 3)
                throw new InvalidDataException("Git returned invalid tree metadata.");
            return new GitEntry(record[(separator + 1)..], metadata[0], metadata[1]);
        }).ToArray());
    }

    private Task<string> QueryAsync(string[] arguments, CancellationToken token) =>
        NativeProcess.RunAsync(executable, directory,
            ["--no-lazy-fetch", "--no-replace-objects", .. arguments], token);

    private static string[] Records(string output)
    {
        if (output.Length == 0)
            return [];
        if (output[^1] != '\0')
            throw new InvalidDataException("Git returned unterminated path output.");
        string[] records = output[..^1].Split('\0');
        if (records.Any(record => record.Length == 0))
            throw new InvalidDataException("Git returned an empty path record.");
        return records;
    }
}
