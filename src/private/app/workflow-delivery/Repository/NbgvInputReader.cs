using Nerdbank.GitVersioning;
using Nerdbank.GitVersioning.Managed;

namespace WorkflowDelivery.Repository;

internal sealed record NbgvInputs(string Commit, string Directory,
    string[] ConfigurationCandidates, string[] Paths);

// This maps committed native answers to consumers, not version height or package versions.
internal sealed class NbgvInputReader(string root)
{
    private readonly string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    // Full projection uses the caller-bound clean HEAD and the same native cloud/ref
    // context as the build. Version height and npm projection remain NBGV operations.
    internal string NpmVersion(GitRevision revision, string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using GitContext context = GitContext.Create(root, engine: GitContext.Engine.ReadOnly);
        if (!context.IsRepository || context.GitCommitId != revision.Commit ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.WorkingTreePath)) != root)
            throw new InvalidDataException(
                "NBGV npm projection requires the selected checkout HEAD.");
        context.RepoRelativeProjectDirectory = directory;
        if (ReadOptions(context, revision, directory)?.Version is null)
            throw new InvalidDataException("NBGV did not resolve the selected product version.");
        var oracle = new VersionOracle(context, CloudBuild.Active);
        token.ThrowIfCancellationRequested();
        return oracle.NpmPackageVersion;
    }

    internal NbgvInputs Read(GitRevision revision, string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (directory != "." && (string.IsNullOrWhiteSpace(directory) ||
            Path.IsPathRooted(directory) || directory.Contains('\\') ||
            directory.Split('/').Any(part => part is "" or "." or "..")))
            throw new InvalidDataException(
                "NBGV requires a canonical repository project directory.");
        if (revision.Commit.Length != 40 || revision.Commit.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("NBGV requires an exact native commit identity.");

        using GitContext context = GitContext.Create(root, revision.Commit,
            GitContext.Engine.ReadOnly);
        if (context is not ManagedGitContext managed || !context.IsRepository ||
            context.GitCommitId != revision.Commit ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.WorkingTreePath)) != root)
            throw new InvalidDataException(
                "NBGV did not bind the required repository and commit.");
        context.RepoRelativeProjectDirectory = directory == "." ? "" : directory;
        bool ignoreCase = managed.Repository.IgnoreCase;
        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (directory != "." && !revision.Entries.Any(entry =>
            entry.Path.StartsWith(directory + "/", comparison)))
            throw new InvalidDataException(
                "The required project directory is absent from the committed inventory.");
        VersionOptions options = ReadOptions(context, revision, directory)
            ?? throw new InvalidDataException("NBGV did not resolve committed version options.");
        if (options.Version is null)
            throw new InvalidDataException("NBGV did not resolve a committed product version.");

        StringComparer comparer = ignoreCase
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = new List<string>();
        string prefix = "";
        AddCandidates();
        if (directory != ".")
            foreach (string part in directory.Split('/'))
            {
                prefix += part + "/";
                AddCandidates();
            }
        var configuration = candidates.ToHashSet(comparer);
        IReadOnlyList<FilterPath>? filters = options.PathFilters;
        bool implicitInclude = filters is null || !filters.Any(filter => filter.IsInclude);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (configuration.Contains(entry.Path) ||
                ((implicitInclude || filters!.Any(filter =>
                    filter.Includes(entry.Path, ignoreCase) || (entry.ObjectType == "commit" &&
                        filter.IncludesChildren(entry.Path, ignoreCase)))) &&
                    (filters is null || !filters.Any(filter =>
                        filter.Excludes(entry.Path, ignoreCase)))))
                paths.Add(entry.Path);
        }
        token.ThrowIfCancellationRequested();
        return new(revision.Commit, directory, candidates.ToArray(),
            paths.Order(StringComparer.Ordinal).ToArray());

        void AddCandidates()
        {
            candidates.Add(prefix + "version.txt");
            candidates.Add(prefix + "version.json");
        }
    }

    private static VersionOptions? ReadOptions(GitContext context, GitRevision revision,
        string directory)
    {
        try
        {
            return context.VersionFile.GetVersion();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException(
                $"Invalid NBGV version options at {revision.Commit}:{directory}.", exception);
        }
    }
}
