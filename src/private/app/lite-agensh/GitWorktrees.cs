using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal sealed class GitWorktrees(
    RunDatabase database,
    CollaborationOperations collaboration,
    ProcessRunner processes
)
{
    internal readonly record struct CandidateWorktree(
        long RequestId,
        string Branch,
        string Path,
        string Commit
    );

    private string Repository => database.Configuration.RepositoryPath;
    internal string IntegrationBranch => $"refs/heads/lite-agensh/{database.RunId}/integration";
    internal string DeliveryBranch => $"refs/heads/lite-agensh/{database.RunId}/delivery";
    internal string IntegrationPath => Path.Combine(database.StatePath, "worktrees", "integration");

    internal static void CreateSchema(
        SqliteConnection connection,
        SqliteTransaction transaction
    ) =>
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS git_resources(
                ref TEXT PRIMARY KEY, path TEXT, original_commit TEXT NOT NULL,
                state TEXT NOT NULL, retained INTEGER NOT NULL DEFAULT 0
            );
            """,
            transaction
        );

    internal async Task InitializeRepositoryAsync(CancellationToken cancellationToken)
    {
        RunConfiguration configuration = database.Configuration;
        if (!Directory.Exists(Repository))
        {
            if (!configuration.CreateRepository)
            {
                throw new OperationException(
                    "unavailable",
                    "Repository does not exist; explicit CreateRepository is required."
                );
            }

            Directory.CreateDirectory(Repository);
        }

        string? repositoryCommon = null;
        string repositoryRoot = "";
        ProcessResult probe = await RunResultAsync(
                Repository,
                ["rev-parse", "--path-format=absolute", "--show-toplevel", "--git-common-dir"],
                cancellationToken
            )
            .ConfigureAwait(false);
        if (probe.ExitCode == 0 && !probe.TimedOut)
        {
            string[] paths = probe.StandardOutput.Split('\n');
            if (
                paths.Length != 3
                || paths[2].Length != 0
                || string.IsNullOrWhiteSpace(paths[0])
                || string.IsNullOrWhiteSpace(paths[1])
            )
            {
                throw new OperationException(
                    "unavailable",
                    "Git repository identity output was incomplete or malformed."
                );
            }

            repositoryRoot = paths[0].Trim();
            if (Paths.AreSame(repositoryRoot, Repository))
            {
                repositoryCommon = paths[1].Trim();
            }
        }

        if (probe.ExitCode != 0 || probe.TimedOut)
        {
            if (
                !configuration.CreateRepository
                || Directory.EnumerateFileSystemEntries(Repository).Any()
            )
            {
                throw new OperationException(
                    "unavailable",
                    "An existing repository or an explicitly authorized empty directory "
                        + "is required."
                );
            }

            await RunAsync(Repository, ["init", "--quiet"], cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!Paths.AreSame(repositoryRoot, Repository))
        {
            if (
                !configuration.CreateRepository
                || Directory.EnumerateFileSystemEntries(Repository).Any()
            )
            {
                throw new OperationException(
                    "unavailable",
                    "RepositoryPath must name a checkout root or an explicitly "
                        + "authorized empty repository directory."
                );
            }

            await RunAsync(Repository, ["init", "--quiet"], cancellationToken)
                .ConfigureAwait(false);
        }

        ProcessResult baseResult = await RunResultAsync(
                Repository,
                [
                    "rev-parse",
                    "--verify",
                    "--end-of-options",
                    configuration.BaseRevision + "^{commit}",
                ],
                cancellationToken
            )
            .ConfigureAwait(false);
        if (
            baseResult.ExitCode != 0
            && configuration.CreateRepository
            && (
                await RunResultAsync(
                        Repository,
                        ["rev-parse", "--verify", "HEAD"],
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            ).ExitCode != 0
        )
        {
            if (
                Directory
                    .EnumerateFileSystemEntries(Repository)
                    .Any(path => Path.GetFileName(path) != ".git")
                || (
                    await RunAsync(
                            Repository,
                            ["ls-files", "--stage", "-z"],
                            cancellationToken
                        )
                        .ConfigureAwait(false)
                ).Length != 0
            )
            {
                throw new OperationException(
                    "unavailable",
                    "Empty repository initialization cannot import existing files."
                );
            }

            if (configuration.BaseRevision != "HEAD")
            {
                throw new OperationException(
                    "invalid_arguments",
                    "A new empty repository requires BaseRevision HEAD."
                );
            }

            await RunAsync(
                    Repository,
                    [
                        "commit",
                        "--quiet",
                        "--allow-empty",
                        "-m",
                        "Initialize LiteAgensh repository",
                    ],
                    cancellationToken
                )
                .ConfigureAwait(false);
            baseResult = await RunResultAsync(
                    Repository,
                    ["rev-parse", "--verify", "HEAD^{commit}"],
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (baseResult.ExitCode != 0)
        {
            throw new OperationException(
                "unavailable",
                "The explicit base revision does not resolve to a commit."
            );
        }

        string baseCommit = baseResult.StandardOutput.Trim();
        if (configuration.BaseRevision != baseCommit)
        {
            database.SaveConfiguration(configuration with { BaseRevision = baseCommit });
        }

        repositoryCommon ??= (
            await RunAsync(
                    Repository,
                    ["rev-parse", "--path-format=absolute", "--git-common-dir"],
                    cancellationToken
                )
                .ConfigureAwait(false)
        ).Trim();
        // Persist each association as it becomes available, even if a later allocation fails.
        database.WriteMetadata(RunMetadata.GitBaseKey, baseCommit);
        database.WriteMetadata(RunMetadata.GitBaseCommitKey, baseCommit);
        await EnsureWorktreeAsync(
                IntegrationBranch, IntegrationPath, baseCommit, repositoryCommon, cancellationToken
            )
            .ConfigureAwait(false);
        database.WriteMetadata(RunMetadata.GitIntegrationBranchKey, IntegrationBranch);
        database.WriteMetadata(RunMetadata.GitIntegrationPathKey, IntegrationPath);
        database.WriteMetadata(RunMetadata.GitDeliveryBranchKey, DeliveryBranch);
        foreach (string worker in database.Configuration.WorkerIds)
        {
            string path = GetWorkerPath(worker);
            await EnsureWorktreeAsync(
                    WorkerBranch(worker), path, baseCommit, repositoryCommon, cancellationToken
                )
                .ConfigureAwait(false);
            database.WriteMetadata(RunMetadata.WorktreeKey(worker), path);
        }

        string currentCommon = (
            await RunAsync(
                    Repository,
                    ["rev-parse", "--path-format=absolute", "--git-common-dir"],
                    cancellationToken
                )
                .ConfigureAwait(false)
        ).Trim();
        if (!Paths.AreSame(repositoryCommon, currentCommon))
        {
            throw new OperationException(
                "conflict",
                "Repository identity changed during worktree initialization."
            );
        }

        database.WriteMetadata(RunMetadata.GitInitializedKey, "true");
    }

    internal string GetWorkerPath(string worker)
    {
        if (!database.Configuration.WorkerIds.Contains(worker, StringComparer.Ordinal))
        {
            throw new OperationException(
                "unknown_actor",
                "Only a configured worker has a worker worktree."
            );
        }

        return Path.Combine(database.StatePath, "worktrees", worker);
    }

    internal string WorkerBranch(string worker)
    {
        _ = GetWorkerPath(worker);
        return $"refs/heads/lite-agensh/{database.RunId}/{worker}";
    }

    private string CandidateBranch(long requestId) =>
        $"refs/heads/lite-agensh/{database.RunId}/candidate-{requestId}";

    private string CandidatePath(long requestId) =>
        Path.Combine(
            database.StatePath,
            "worktrees",
            "candidate-" + requestId.ToString(CultureInfo.InvariantCulture)
        );

    internal async Task<CandidateWorktree> CreateCandidateAsync(
        long requestId,
        long changeId,
        string parent,
        string submitted,
        CancellationToken token
    )
    {
        string candidateBranch = CandidateBranch(requestId);
        string path = CandidatePath(requestId);
        await EnsureWorktreeAsync(candidateBranch, path, parent, null, token).ConfigureAwait(false);
        ProcessResult merge = await RunResultAsync(
                path,
                ["merge", "--no-ff", "--no-edit", "-m", $"Integrate change {changeId}", submitted],
                token
            )
            .ConfigureAwait(false);
        await WriteDiagnosticAsync(
                "merge-" + requestId.ToString(CultureInfo.InvariantCulture),
                merge,
                token
            )
            .ConfigureAwait(false);
        if (merge.ExitCode != 0 || merge.TimedOut)
        {
            throw new OperationException(
                "conflict",
                $"Candidate merge failed; conflict state is retained at {path}. "
                    + $"{merge.StandardError.Trim()}"
            );
        }

        string candidate = await ResolveAsync(candidateBranch, token).ConfigureAwait(false);
        if (candidate == parent)
        {
            throw new OperationException(
                "conflict",
                "Submitted work is already contained in integration."
            );
        }

        return new(requestId, candidateBranch, path, candidate);
    }

    internal async Task AdvanceIntegrationAsync(
        CandidateWorktree candidate,
        string workerBranch,
        string submitted,
        string parent,
        CancellationToken token
    )
    {
        token.ThrowIfCancellationRequested();
        // Once the durable advance starts, finish syncing the worktree even if cancelled.
        ProcessResult updateRef = await RunResultAsync(
                Repository,
                ["update-ref", "--stdin"],
                CancellationToken.None,
                standardInput: $"verify {workerBranch} {submitted}\n"
                    + $"verify {candidate.Branch} {candidate.Commit}\n"
                    + $"update {IntegrationBranch} {candidate.Commit} {parent}\n"
            )
            .ConfigureAwait(false);
        if (updateRef.ExitCode != 0 || updateRef.TimedOut)
        {
            await WriteDiagnosticAsync(
                    "update-ref-" + candidate.RequestId.ToString(CultureInfo.InvariantCulture),
                    updateRef,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            if (
                !updateRef.TimedOut
                && (
                    await ResolveAsync(workerBranch, CancellationToken.None)
                        .ConfigureAwait(false) != submitted
                    || await ResolveAsync(candidate.Branch, CancellationToken.None)
                        .ConfigureAwait(false) != candidate.Commit
                    || await ResolveAsync(IntegrationBranch, CancellationToken.None)
                        .ConfigureAwait(false) != parent
                )
            )
            {
                throw new OperationException(
                    "stale_candidate",
                    "Integration, candidate or submitted worker head moved before integration "
                        + "advance."
                );
            }

            throw new OperationException(
                "unavailable",
                $"Git update-ref failed (exit: {updateRef.ExitCode}; "
                    + $"timeout: {updateRef.TimedOut}): {updateRef.StandardError.Trim()}"
            );
        }

        await RunAsync(
                IntegrationPath, ["reset", "--hard", candidate.Commit], CancellationToken.None
            )
            .ConfigureAwait(false);
    }

    internal async Task RecoverIntegrationWorktreeAsync(
        string? parent,
        string candidate,
        CancellationToken token
    )
    {
        await VerifyWorktreeAsync(IntegrationBranch, IntegrationPath, null, token)
            .ConfigureAwait(false);
        string tree = await ResolveTreeAsync(candidate, token).ConfigureAwait(false);
        string index = (
            await RunAsync(IntegrationPath, ["write-tree"], token).ConfigureAwait(false)
        ).Trim();
        string? previousTree = parent is null
            ? null
            : await ResolveTreeAsync(parent, token).ConfigureAwait(false);
        if (
            (index != tree && index != previousTree)
            || (
                await RunResultAsync(IntegrationPath, ["diff", "--quiet", "--"], token)
                    .ConfigureAwait(false)
            ).ExitCode != 0
            || (
                await RunAsync(
                        IntegrationPath,
                        ["ls-files", "--others", "--exclude-standard"],
                        token
                    )
                    .ConfigureAwait(false)
            ).Length != 0
        )
        {
            throw new OperationException(
                "conflict",
                "Interrupted integration has an ambiguous worktree/index; "
                    + "preserve it for inspection."
            );
        }

        await RunAsync(IntegrationPath, ["reset", "--hard", candidate], token)
            .ConfigureAwait(false);
    }

    internal async Task RequireReadyIntegrationWorktreeAsync(CancellationToken token)
    {
        await VerifyWorktreeAsync(IntegrationBranch, IntegrationPath, null, token)
            .ConfigureAwait(false);
        await RequireCleanAsync(IntegrationPath, token).ConfigureAwait(false);
    }

    internal async Task<JsonArray> CleanupOwnedResourcesAsync(
        Action<SqliteConnection, SqliteTransaction> requireSettledRequests,
        CancellationToken cancellationToken
    )
    {
        bool cleaning = false;
        try
        {
            List<(string Ref, string Path)> resources = [];
            using (SqliteConnection connection = database.Open())
            using (
                SqliteTransactionScope transaction = Sql.BeginTransaction(
                    connection, operation: "CleanupAsync"
                )
            )
            {
                // Close admission in the same transaction that verifies requests are settled.
                requireSettledRequests(connection, transaction);

                using SqliteCommand command = Sql.Command(
                    connection,
                    "SELECT ref,path FROM git_resources WHERE retained=0 AND "
                        + "state='ready' ORDER BY ref;",
                    transaction
                );
                using (ObservedSqliteReader reader = Sql.Reader(command))
                {
                    while (reader.Read())
                    {
                        resources.Add((reader.GetString(0), reader.GetString(1)));
                    }
                }

                Sql.SetMetadata(connection, transaction, RunMetadata.GitCleaningKey, "true");
                transaction.Commit();
                cleaning = true;
            }

            // Preflight every resource before removing any; dirty worker work is never discarded.
            foreach ((string reference, string path) in resources)
            {
                await VerifyWorktreeAsync(reference, path, null, cancellationToken)
                    .ConfigureAwait(false);
                await RequireCleanAsync(path, cancellationToken).ConfigureAwait(false);
            }

            JsonArray removed = [];
            foreach ((string reference, string path) in resources)
            {
                string head = await ResolveAsync(reference, cancellationToken)
                    .ConfigureAwait(false);
                await RunAsync(Repository, ["worktree", "remove", "--", path], cancellationToken)
                    .ConfigureAwait(false);
                await RunAsync(Repository, ["update-ref", "-d", reference, head], cancellationToken)
                    .ConfigureAwait(false);
                using SqliteConnection connection = database.Open();
                using SqliteTransactionScope transaction = Sql.BeginTransaction(
                    connection, operation: "CleanupAsync"
                );
                Sql.Execute(
                    connection,
                    "UPDATE git_resources SET state='removed' WHERE ref=$ref;",
                    transaction,
                    ("$ref", reference)
                );
                PublishEvent(
                    connection,
                    transaction,
                    "cleanup",
                    $"Removed owned worktree {path} and ref {reference}."
                );
                transaction.Commit();
                removed.Add(reference);
            }

            database.WriteMetadata(RunMetadata.GitCleanedKey, "true");
            return removed;
        }
        finally
        {
            if (cleaning)
            {
                database.WriteMetadata(RunMetadata.GitCleaningKey, "false");
            }
        }
    }

    private async Task EnsureWorktreeAsync(
        string reference,
        string path,
        string commit,
        string? repositoryCommon,
        CancellationToken token
    )
    {
        string? state;
        using (SqliteConnection connection = database.Open())
        {
            state =
                Sql.Scalar(
                    connection,
                    "SELECT state FROM git_resources WHERE ref=$ref;",
                    null,
                    ("$ref", reference)
                ) as string;
            if (state is null)
            {
                if (
                    Directory.Exists(path)
                    || File.Exists(path)
                    || (
                        await RunResultAsync(
                                Repository,
                                ["show-ref", "--verify", "--quiet", reference],
                                token
                            )
                            .ConfigureAwait(false)
                    ).ExitCode == 0
                )
                {
                    throw new OperationException(
                        "conflict",
                        "An unowned ref or worktree already occupies an intended run resource."
                    );
                }

                Sql.Execute(
                    connection,
                    "INSERT INTO git_resources(ref,path,original_commit,state) "
                        + "VALUES($ref,$path,$commit,'planned');",
                    null,
                    ("$ref", reference),
                    ("$path", path),
                    ("$commit", commit)
                );
            }
        }

        if (state == "removed")
        {
            throw new OperationException(
                "unavailable",
                "This run's worktrees were explicitly cleaned up."
            );
        }

        if (state == "ready")
        {
            await VerifyWorktreeAsync(reference, path, repositoryCommon, token)
                .ConfigureAwait(false);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (state is null)
        {
            await RunAsync(
                    Repository,
                    [
                        "worktree",
                        "add",
                        "--quiet",
                        "-b",
                        reference["refs/heads/".Length..],
                        path,
                        commit,
                    ],
                    token
                )
                .ConfigureAwait(false);
        }
        else
        {
            ProcessResult exists = await RunResultAsync(
                    Repository,
                    ["show-ref", "--verify", "--quiet", reference],
                    token
                )
                .ConfigureAwait(false);
            if (exists.ExitCode != 0)
            {
                await RunAsync(
                        Repository,
                        ["update-ref", reference, commit, new string('0', commit.Length)],
                        token
                    )
                    .ConfigureAwait(false);
            }
            else if (await ResolveAsync(reference, token).ConfigureAwait(false) != commit)
            {
                throw new OperationException(
                    "conflict",
                    "An incompletely allocated run ref has moved; ownership requires inspection."
                );
            }

            if (!Directory.Exists(path))
            {
                await RunAsync(
                        Repository,
                        ["worktree", "add", "--quiet", path, reference["refs/heads/".Length..]],
                        token
                    )
                    .ConfigureAwait(false);
            }
        }

        await VerifyWorktreeAsync(reference, path, repositoryCommon, token).ConfigureAwait(false);
        using SqliteConnection store = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(store);
        Sql.Execute(
            store,
            "UPDATE git_resources SET state='ready' WHERE ref=$ref;",
            transaction,
            ("$ref", reference)
        );
        PublishEvent(
            store, transaction, "worktree", $"Allocated owned worktree {path} on {reference}."
        );
        transaction.Commit();
    }

    private async Task VerifyWorktreeAsync(
        string reference,
        string path,
        string? repositoryCommon,
        CancellationToken token
    )
    {
        string worktrees = Path.Combine(database.StatePath, "worktrees");
        using (SqliteConnection connection = database.Open())
        {
            string? saved =
                Sql.Scalar(
                    connection,
                    "SELECT path FROM git_resources WHERE ref=$ref AND state<>'removed';",
                    null,
                    ("$ref", reference)
                ) as string;
            if (
                !Paths.AreSame(path, saved)
                || !Paths.IsSameOrUnder(path, worktrees)
                || Paths.AreSame(path, worktrees)
                || Paths.AreSame(path, Repository)
            )
            {
                throw new OperationException(
                    "conflict",
                    "Worktree is not an identified owned resource."
                );
            }
        }

        string[] paths = (
            await RunAsync(
                    path,
                    [
                        "rev-parse",
                        "--path-format=absolute",
                        "--show-toplevel",
                        "--git-common-dir",
                        "--symbolic-full-name",
                        "HEAD",
                    ],
                    token
                )
                .ConfigureAwait(false)
        ).Split('\n');
        if (
            paths.Length != 4
            || paths[3].Length != 0
            || string.IsNullOrWhiteSpace(paths[0])
            || string.IsNullOrWhiteSpace(paths[1])
            || string.IsNullOrWhiteSpace(paths[2])
        )
        {
            throw new OperationException(
                "unavailable",
                "Git worktree identity output was incomplete or malformed."
            );
        }

        string root = paths[0].Trim();
        string common = paths[1].Trim();
        string branch = paths[2].Trim();
        repositoryCommon ??= (
            await RunAsync(
                    Repository,
                    ["rev-parse", "--path-format=absolute", "--git-common-dir"],
                    token
                )
                .ConfigureAwait(false)
        ).Trim();
        if (
            !Paths.AreSame(root, path)
            || branch != reference
            || !Paths.AreSame(common, repositoryCommon)
        )
        {
            throw new OperationException(
                "conflict",
                "Worktree identity does not match the saved run association."
            );
        }
    }

    private async Task RequireCleanAsync(string path, CancellationToken token)
    {
        string status = await RunAsync(
                path,
                ["status", "--porcelain=v1", "--untracked-files=all"],
                token
            )
            .ConfigureAwait(false);
        if (status.Length != 0)
        {
            throw new OperationException(
                "conflict",
                $"Owned worktree has unresolved or uncommitted work; preserve and inspect {path}."
            );
        }
    }

    internal async Task RequireExactTreeAsync(string path, string commit, CancellationToken token)
    {
        string output = await RunAsync(
                path,
                ["status", "--porcelain=v2", "--branch", "--untracked-files=no"],
                token
            )
            .ConfigureAwait(false);
        string? current = null;
        bool clean = true;
        foreach (string raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                current = line["# branch.oid ".Length..];
            }
            else if (!line.StartsWith("# ", StringComparison.Ordinal))
            {
                clean = false;
            }
        }

        if (!clean || current != commit)
        {
            throw new OperationException(
                "stale_candidate",
                "Checked commit or tracked tree/index changed; old check evidence cannot "
                    + "advance integration."
            );
        }
    }

    internal async Task RetainDeliveryAsync(string head, CancellationToken token)
    {
        string worktrees = await RunAsync(
                Repository,
                ["worktree", "list", "--porcelain", "-z"],
                token
            )
            .ConfigureAwait(false);
        if (worktrees.Split('\0').Contains("branch " + DeliveryBranch, StringComparer.Ordinal))
        {
            throw new OperationException(
                "conflict",
                "Delivery ref is checked out outside the run; it will not be advanced."
            );
        }

        bool known;
        using (SqliteConnection connection = database.Open())
        {
            known =
                Sql.Scalar(
                    connection,
                    "SELECT ref FROM git_resources WHERE ref=$ref;",
                    null,
                    ("$ref", DeliveryBranch)
                )
                    is not null;
        }

        ProcessResult existing = await RunResultAsync(
                Repository,
                ["show-ref", "--verify", "--quiet", DeliveryBranch],
                token
            )
            .ConfigureAwait(false);
        if (!known && existing.ExitCode == 0)
        {
            throw new OperationException("conflict", "Delivery ref exists without run ownership.");
        }

        string expected =
            existing.ExitCode == 0
                ? await ResolveAsync(DeliveryBranch, token).ConfigureAwait(false)
                : new string('0', head.Length);
        if (
            existing.ExitCode == 0
            && (
                await RunResultAsync(
                        Repository,
                        ["merge-base", "--is-ancestor", expected, head],
                        token
                    )
                    .ConfigureAwait(false)
            ).ExitCode != 0
        )
        {
            throw new OperationException(
                "conflict",
                "Retained delivery has diverged; it will not be force-updated."
            );
        }

        if (!known)
        {
            using SqliteConnection connection = database.Open();
            Sql.Execute(
                connection,
                "INSERT INTO git_resources(ref,path,original_commit,state,retained) "
                    + "VALUES($ref,NULL,$head,'planned',1);",
                null,
                ("$ref", DeliveryBranch),
                ("$head", head)
            );
        }

        await RunAsync(Repository, ["update-ref", DeliveryBranch, head, expected], token)
            .ConfigureAwait(false);
        using SqliteConnection store = database.Open();
        Sql.Execute(
            store,
            "UPDATE git_resources SET state='ready' WHERE ref=$ref;",
            null,
            ("$ref", DeliveryBranch)
        );
    }

    internal Task<string> ReadChangeDiffAsync(string head, CancellationToken token) =>
        RunAsync(
            Repository,
            [
                "diff",
                "--no-ext-diff",
                "--no-textconv",
                database.Configuration.BaseRevision,
                head,
                "--",
            ],
            token
        );

    internal async Task<string[]> ReadChangedPathsAsync(
        string parent,
        string head,
        string? mergedCandidate,
        CancellationToken token
    )
    {
        string[] arguments = mergedCandidate is null
            ? ["diff", "--no-renames", "--name-only", "-z", parent + "..." + head, "--"]
            : ["diff", "--no-renames", "--name-only", "-z", parent, mergedCandidate, "--"];
        string files = await RunAsync(Repository, arguments, token).ConfigureAwait(false);
        return files.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    internal async Task<string> ResolveTreeAsync(string commit, CancellationToken token) =>
        (await RunAsync(Repository, ["rev-parse", commit + "^{tree}"], token)
            .ConfigureAwait(false)).Trim();

    internal async Task<string> ResolveAsync(string revision, CancellationToken token) =>
        (
            await RunAsync(
                    Repository,
                    ["rev-parse", "--verify", "--end-of-options", revision + "^{commit}"],
                    token
                )
                .ConfigureAwait(false)
        ).Trim();

    private async Task<ProcessResult> RunResultAsync(
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken token,
        string? standardInput = null
    )
    {
        string hooks = Path.Combine(database.StatePath, "git-hooks");
        Directory.CreateDirectory(hooks);
        string[] options =
        [
            "-c",
            "user.name=LiteAgensh",
            "-c",
            "user.email=lite-agensh@localhost",
            "-c",
            "commit.gpgSign=false",
            "-c",
            "core.hooksPath=" + hooks,
            "-c",
            "core.autocrlf=false",
            "-c",
            "core.quotePath=false",
        ];
        return await processes
            .RunAsync(
                "git",
                [.. options, .. arguments],
                path,
                TimeSpan.FromSeconds(60),
                standardInput,
                token
            )
            .ConfigureAwait(false);
    }

    private async Task<string> RunAsync(
        string path,
        string[] arguments,
        CancellationToken token
    )
    {
        ProcessResult result = await RunResultAsync(path, arguments, token).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new OperationException(
                "unavailable",
                $"Git {arguments[0]} failed "
                    + $"(exit: {result.ExitCode}; timeout: {result.TimedOut}): "
                    + result.StandardError.Trim()
            );
        }

        return result.StandardOutput;
    }

    private async Task WriteDiagnosticAsync(
        string name,
        ProcessResult result,
        CancellationToken token
    )
    {
        string directory = Path.Combine(database.StatePath, "logs", "git");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
                Path.Combine(directory, name + ".log"),
                $"exit: {result.ExitCode}\ntimeout: {result.TimedOut}\n"
                    + $"stdout:\n{result.StandardOutput}\nstderr:\n{result.StandardError}",
                Encoding.UTF8,
                token
            )
            .ConfigureAwait(false);
    }

    internal void PublishEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string kind,
        string payload,
        string? target = null
    )
    {
        string key = "git:" + kind + ":" + Guid.NewGuid().ToString("N");
        string serialized = new JsonObject { ["kind"] = kind, ["summary"] = payload }.ToJsonString(
            Json.Options
        );
        foreach (string recipient in target is null ? database.Configuration.WorkerIds : [target])
        {
            collaboration.EnqueueEvent(
                connection,
                transaction,
                recipient,
                kind,
                key + ":" + recipient,
                serialized
            );
        }
    }

}
