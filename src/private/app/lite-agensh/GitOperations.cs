using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

public sealed class GitOperations
{
    private readonly RunDatabase database;
    private readonly CollaborationOperations collaboration;
    private readonly ProcessRunner processes;
    private string Repository => database.Configuration.RepositoryPath;
    private string IntegrationBranch => $"refs/heads/lite-agensh/{database.RunId}/integration";
    private string DeliveryBranch => $"refs/heads/lite-agensh/{database.RunId}/delivery";
    private string IntegrationPath => Path.Combine(database.StatePath, "worktrees", "integration");
    private string Stamp => database.Now.ToString("O", CultureInfo.InvariantCulture);

    public GitOperations(RunDatabase database, CollaborationOperations collaboration)
    {
        this.database = database;
        this.collaboration = collaboration;
        processes = new ProcessRunner(database);
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection,
            """
            CREATE TABLE IF NOT EXISTS changes(
                id INTEGER PRIMARY KEY AUTOINCREMENT, author TEXT NOT NULL, branch TEXT NOT NULL,
                head TEXT NOT NULL, title TEXT NOT NULL, body TEXT NOT NULL, task_ids TEXT NOT NULL,
                state TEXT NOT NULL DEFAULT 'open', created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS change_comments(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                change_id INTEGER NOT NULL REFERENCES changes(id),
                actor TEXT NOT NULL, body TEXT NOT NULL, created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS reviews(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                change_id INTEGER NOT NULL REFERENCES changes(id),
                actor TEXT NOT NULL, head TEXT NOT NULL, decision TEXT NOT NULL,
                body TEXT NOT NULL, created_at TEXT NOT NULL, stale INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS checks(
                id INTEGER PRIMARY KEY AUTOINCREMENT, change_id INTEGER REFERENCES changes(id),
                kind TEXT NOT NULL, commit_hash TEXT NOT NULL, tree TEXT NOT NULL,
                command_json TEXT NOT NULL, status TEXT NOT NULL, exit_code INTEGER,
                log_path TEXT NOT NULL, created_at TEXT NOT NULL, batch TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS integration_requests(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                change_id INTEGER NOT NULL REFERENCES changes(id),
                actor TEXT NOT NULL, head TEXT NOT NULL, state TEXT NOT NULL,
                result_code TEXT, message TEXT, integrated_commit TEXT,
                created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
                parent_commit TEXT, candidate_commit TEXT, ready INTEGER NOT NULL DEFAULT 0
            );
            CREATE UNIQUE INDEX IF NOT EXISTS integration_pending
                ON integration_requests(change_id,head) WHERE state IN ('pending','running');
            CREATE TABLE IF NOT EXISTS git_resources(
                ref TEXT PRIMARY KEY, path TEXT, original_commit TEXT NOT NULL,
                state TEXT NOT NULL, retained INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS git_health(
                singleton INTEGER PRIMARY KEY CHECK(singleton=1), commit_hash TEXT NOT NULL,
                passed INTEGER NOT NULL, red_since TEXT, broken_commit TEXT NOT NULL,
                alerted INTEGER NOT NULL, lifted INTEGER NOT NULL, checked_at TEXT NOT NULL
            );
            """,
            transaction
        );
        transaction.Commit();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        RequireSettledProcessOwnership();
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

        ProcessResult probe = await GitResultAsync(
                Repository,
                ["rev-parse", "--show-toplevel"],
                cancellationToken
            )
            .ConfigureAwait(false);
        if (probe.ExitCode != 0)
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

            await GitAsync(Repository, ["init", "--quiet"], cancellationToken)
                .ConfigureAwait(false);
        }
        else if (!SamePath(probe.StandardOutput.Trim(), Repository))
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

            await GitAsync(Repository, ["init", "--quiet"], cancellationToken)
                .ConfigureAwait(false);
        }

        ProcessResult baseResult = await GitResultAsync(
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
                await GitResultAsync(
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

            await GitAsync(
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
            baseResult = await GitResultAsync(
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

        database.WriteMetadata("git:base", baseCommit);
        database.WriteMetadata("git:base_commit", baseCommit);
        await EnsureWorktreeAsync(IntegrationBranch, IntegrationPath, baseCommit, cancellationToken)
            .ConfigureAwait(false);
        database.WriteMetadata("git:integration_branch", IntegrationBranch);
        database.WriteMetadata("git:integration_path", IntegrationPath);
        database.WriteMetadata("git:delivery_branch", DeliveryBranch);
        foreach (string worker in database.Configuration.WorkerIds)
        {
            string path = GetWorkerPath(worker);
            await EnsureWorktreeAsync(WorkerBranch(worker), path, baseCommit, cancellationToken)
                .ConfigureAwait(false);
            database.WriteMetadata("worktree:" + worker, path);
        }

        database.WriteMetadata("git:initialized", "true");
    }

    public string GetWorkerPath(string worker)
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

    public async Task<JsonObject> InvokeAsync(
        string actor,
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!database.Members.Contains(actor, StringComparer.Ordinal))
            {
                throw new OperationException("unknown_actor", "Actor is not a member of this run.");
            }

            return name switch
            {
                "change_write" => await WriteChangeAsync(actor, arguments, cancellationToken)
                    .ConfigureAwait(false),
                "change_read" => await ReadChangeAsync(arguments, cancellationToken)
                    .ConfigureAwait(false),
                "change_list" => ListChanges(arguments),
                "change_review" => Review(actor, arguments),
                "check_read" => ReadChecks(arguments),
                _ => Operation.Failure("unavailable", "Unknown Git operation."),
            };
        }
        catch (OperationException exception)
        {
            return Operation.Failure(exception.Code, exception.Message);
        }
    }

    public async Task ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        RequireInitialized();
        await RecoverRequestsAsync(cancellationToken).ConfigureAwait(false);
        await EvaluateHealthCoreAsync(forceChecks: false, cancellationToken).ConfigureAwait(false);
        List<long> ids = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id FROM integration_requests WHERE state='pending' ORDER BY id;"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        foreach (long id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireSettledProcessOwnership();
            try
            {
                await IntegrateAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!await TryRecoverAdvanceAsync(id).ConfigureAwait(false))
                {
                    FinishRequest(
                        id,
                        false,
                        "unavailable",
                        "Integration cancelled; all worker work is retained.",
                        null
                    );
                }

                throw;
            }
            catch (OperationException exception)
            {
                if (!await TryRecoverAdvanceAsync(id).ConfigureAwait(false))
                {
                    FinishRequest(id, false, exception.Code, exception.Message, null);
                }
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                if (!await TryRecoverAdvanceAsync(id).ConfigureAwait(false))
                {
                    FinishRequest(id, false, "unavailable", exception.Message, null);
                }
            }
        }
    }

    public async Task<JsonObject> EvaluateHealthAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        RequireInitialized();
        try
        {
            return Operation.Success(
                await EvaluateHealthCoreAsync(forceChecks: true, cancellationToken)
                    .ConfigureAwait(false)
            );
        }
        catch (OperationException exception)
        {
            return Operation.Failure(exception.Code, exception.Message);
        }
    }

    public async Task<JsonObject> FinalizeAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        RequireInitialized();
        try
        {
            RequireQuiescent(allowFinalizing: true);
            using (SqliteConnection connection = database.Open())
            {
                if (
                    Convert.ToInt64(
                        Sql.Scalar(
                            connection,
                            "SELECT COUNT(*) FROM integration_requests WHERE state IN "
                                + "('pending','running');"
                        ),
                        CultureInfo.InvariantCulture
                    ) != 0
                )
                {
                    throw new OperationException(
                        "conflict",
                        "Pending integrations must be processed or inspected before finalization."
                    );
                }
            }

            await VerifyWorktreeAsync(IntegrationBranch, IntegrationPath, cancellationToken)
                .ConfigureAwait(false);
            await RequireCleanAsync(IntegrationPath, cancellationToken).ConfigureAwait(false);
            string head = await ResolveAsync(IntegrationBranch, cancellationToken)
                .ConfigureAwait(false);
            string batch = Guid.NewGuid().ToString("N");
            bool passed;
            string failureCode = "check_failed";
            string failureMessage =
                "Final checks failed; local delivery and unresolved work are retained.";
            try
            {
                passed = await RunChecksAsync(
                        null,
                        "final",
                        head,
                        IntegrationPath,
                        database.Configuration.FinalChecks,
                        batch,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                await RequireExactTreeAsync(IntegrationPath, head, cancellationToken)
                    .ConfigureAwait(false);
                if (
                    await ResolveAsync(IntegrationBranch, cancellationToken).ConfigureAwait(false)
                    != head
                )
                {
                    throw new OperationException(
                        "stale_candidate",
                        "Integration moved during final checks."
                    );
                }
            }
            catch (OperationException exception)
            {
                RequireSettledProcessOwnership();
                passed = false;
                failureCode = exception.Code;
                failureMessage =
                    exception.Message
                    + " The captured delivery commit is retained but is not accepted.";
            }

            await RetainDeliveryAsync(head, cancellationToken).ConfigureAwait(false);
            JsonObject report = BuildDeliveryReport(head, batch, passed);
            using SqliteConnection store = database.Open();
            using SqliteTransactionScope transaction = Sql.BeginTransaction(store);
            SetMeta(store, transaction, "git:delivery_report", report.ToJsonString(Json.Options));
            SetMeta(store, transaction, "git:finalized", "true");
            Event(
                store,
                transaction,
                "delivery",
                $"Local delivery {DeliveryBranch} at {head}: "
                    + $"final checks {(passed ? "passed" : "failed")}."
            );
            transaction.Commit();
            JsonObject result = passed
                ? Operation.Success(report)
                : Operation.Failure(failureCode, failureMessage);
            result["data"] = report;
            return result;
        }
        catch (OperationException exception)
        {
            return Operation.Failure(exception.Code, exception.Message);
        }
    }

    public async Task<JsonObject> CleanupAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        bool cleaning = false;
        try
        {
            RequireQuiescent(allowFinalizing: false);
            if (database.ReadMetadata("git:finalized") != "true")
            {
                throw new OperationException(
                    "conflict",
                    "Inspect and finalize retained delivery before explicit cleanup."
                );
            }

            List<(string Ref, string Path)> resources = [];
            using (SqliteConnection connection = database.Open())
            using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
            {
                if (
                    Convert.ToInt64(
                        Sql.Scalar(
                            connection,
                            "SELECT COUNT(*) FROM integration_requests WHERE state IN "
                                + "('pending','running');",
                            transaction
                        ),
                        CultureInfo.InvariantCulture
                    ) != 0
                )
                {
                    throw new OperationException(
                        "conflict",
                        "Unsettled integration requests block cleanup."
                    );
                }

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

                SetMeta(connection, transaction, "git:cleaning", "true");
                transaction.Commit();
                cleaning = true;
            }

            // Preflight every resource before removing any; dirty worker work is never discarded.
            foreach ((string reference, string path) in resources)
            {
                await VerifyWorktreeAsync(reference, path, cancellationToken).ConfigureAwait(false);
                await RequireCleanAsync(path, cancellationToken).ConfigureAwait(false);
            }

            JsonArray removed = [];
            foreach ((string reference, string path) in resources)
            {
                string head = await ResolveAsync(reference, cancellationToken)
                    .ConfigureAwait(false);
                await GitAsync(Repository, ["worktree", "remove", "--", path], cancellationToken)
                    .ConfigureAwait(false);
                await GitAsync(Repository, ["update-ref", "-d", reference, head], cancellationToken)
                    .ConfigureAwait(false);
                using SqliteConnection connection = database.Open();
                using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                Sql.Execute(
                    connection,
                    "UPDATE git_resources SET state='removed' WHERE ref=$ref;",
                    transaction,
                    ("$ref", reference)
                );
                Event(
                    connection,
                    transaction,
                    "cleanup",
                    $"Removed owned worktree {path} and ref {reference}."
                );
                transaction.Commit();
                removed.Add(reference);
            }

            database.WriteMetadata("git:cleaned", "true");
            return Operation.Success(
                new
                {
                    removed,
                    retainedDeliveryRef = DeliveryBranch,
                    statePath = database.StatePath,
                }
            );
        }
        catch (OperationException exception)
        {
            return Operation.Failure(exception.Code, exception.Message);
        }
        finally
        {
            if (cleaning)
            {
                database.WriteMetadata("git:cleaning", "false");
            }
        }
    }

    private async Task<JsonObject> WriteChangeAsync(
        string actor,
        JsonObject arguments,
        CancellationToken token
    )
    {
        RequireInitialized();
        string action = Text(arguments, "action");
        switch (action)
        {
            case "submit":
                {
                    Fields(arguments, ["action", "title", "body", "taskIds"]);
                    string title = Text(arguments, "title");
                    string body = Text(arguments, "body");
                    JsonArray tasks = TaskIds(arguments);
                    string branch = WorkerBranch(actor);
                    string head = await ResolveAsync(branch, token).ConfigureAwait(false);
                    using SqliteConnection connection = database.Open();
                    using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                    RequireWritable(connection, transaction);
                    ValidateTasks(connection, transaction, tasks);
                    Sql.Execute(
                        connection,
                        """
                    INSERT INTO changes(author,branch,head,title,body,task_ids,created_at,
                        updated_at)
                    VALUES($actor,$branch,$head,$title,$body,$tasks,$time,$time);
                    """,
                        transaction,
                        ("$actor", actor),
                        ("$branch", branch),
                        ("$head", head),
                        ("$title", title),
                        ("$body", body),
                        ("$tasks", tasks.ToJsonString()),
                        ("$time", Stamp)
                    );
                    long id = LastId(connection, transaction);
                    Event(
                        connection,
                        transaction,
                        "change",
                        $"{actor} submitted change {id}: {title} ({head})."
                    );
                    transaction.Commit();
                    return Operation.Success(Change(id));
                }
            case "update":
                {
                    Fields(arguments, ["action", "id", "title", "body", "taskIds", "head"]);
                    long id = Id(arguments, "id");
                    if (
                        !arguments.Any(pair => pair.Key is "title" or "body" or "taskIds" or "head")
                    )
                    {
                        throw Invalid("An update requires at least one updated field.");
                    }

                    string? title = OptionalText(arguments, "title");
                    string? body = OptionalText(arguments, "body");
                    string? head = OptionalText(arguments, "head");
                    JsonArray? tasks = arguments.ContainsKey("taskIds") ? TaskIds(arguments) : null;
                    if (
                        head is not null
                        && head != await ResolveAsync(WorkerBranch(actor), token)
                            .ConfigureAwait(false)
                    )
                    {
                        throw new OperationException(
                            "stale_candidate",
                            "The new head must be the current bound worker head."
                        );
                    }

                    using SqliteConnection connection = database.Open();
                    using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                    RequireWritable(connection, transaction);
                    JsonObject change = Change(connection, transaction, id);
                    RequireAuthor(change, actor);
                    RequireOpen(change);
                    RequireNotRunning(connection, transaction, id);
                    if (tasks is not null)
                    {
                        ValidateTasks(connection, transaction, tasks);
                    }

                    if (head is not null && head != change["head"]!.GetValue<string>())
                    {
                        Sql.Execute(
                            connection,
                            "UPDATE reviews SET stale=1 WHERE change_id=$id;",
                            transaction,
                            ("$id", id)
                        );
                    }

                    Sql.Execute(
                        connection,
                        """
                    UPDATE changes SET title=$title,body=$body,task_ids=$tasks,head=$head,
                        updated_at=$time WHERE id=$id;
                    """,
                        transaction,
                        ("$title", title ?? change["title"]!.GetValue<string>()),
                        ("$body", body ?? change["body"]!.GetValue<string>()),
                        ("$tasks", (tasks ?? change["taskIds"]!.AsArray()).ToJsonString()),
                        ("$head", head ?? change["head"]!.GetValue<string>()),
                        ("$time", Stamp),
                        ("$id", id)
                    );
                    Event(
                        connection,
                        transaction,
                        "change",
                        $"{actor} updated change {id}; approvals bind only to its exact head."
                    );
                    transaction.Commit();
                    return Operation.Success(Change(id));
                }
            case "comment":
                {
                    Fields(arguments, ["action", "id", "body"]);
                    long id = Id(arguments, "id");
                    string body = Text(arguments, "body");
                    using SqliteConnection connection = database.Open();
                    using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                    RequireWritable(connection, transaction);
                    _ = Change(connection, transaction, id);
                    Sql.Execute(
                        connection,
                        """
                    INSERT INTO change_comments(change_id,actor,body,created_at)
                    VALUES($id,$actor,$body,$time);
                    """,
                        transaction,
                        ("$id", id),
                        ("$actor", actor),
                        ("$body", body),
                        ("$time", Stamp)
                    );
                    Event(
                        connection,
                        transaction,
                        "change-comment",
                        $"{actor} commented on change {id}: {body}"
                    );
                    transaction.Commit();
                    return Operation.Success(Change(id));
                }
            case "integrate":
                {
                    Fields(arguments, ["action", "id", "head"]);
                    long id = Id(arguments, "id");
                    string head = Text(arguments, "head");
                    using SqliteConnection connection = database.Open();
                    using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                    RequireWritable(connection, transaction);
                    JsonObject change = Change(connection, transaction, id);
                    RequireOpen(change);
                    if (head != change["head"]!.GetValue<string>())
                    {
                        throw new OperationException(
                            "stale_candidate",
                            "Integration requires the exact recorded head."
                        );
                    }

                    object? existing = Sql.Scalar(
                        connection,
                        "SELECT id FROM integration_requests WHERE change_id=$id AND "
                            + "head=$head AND state IN ('pending','running');",
                        transaction,
                        ("$id", id),
                        ("$head", head)
                    );
                    long requestId;
                    if (existing is not null)
                    {
                        requestId = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        Sql.Execute(
                            connection,
                            """
                        INSERT INTO integration_requests(change_id,actor,head,state,
                            created_at,updated_at)
                        VALUES($id,$actor,$head,'pending',$time,$time);
                        """,
                            transaction,
                            ("$id", id),
                            ("$actor", actor),
                            ("$head", head),
                            ("$time", Stamp)
                        );
                        requestId = LastId(connection, transaction);
                        Event(
                            connection,
                            transaction,
                            "integration-request",
                            $"{actor} queued integration {requestId} for change {id} ({head})."
                        );
                    }

                    string state = (string)
                        Sql.Scalar(
                            connection,
                            "SELECT state FROM integration_requests WHERE id=$id;",
                            transaction,
                            ("$id", requestId)
                        )!;
                    transaction.Commit();
                    return Operation.Success(
                        new
                        {
                            requestId,
                            changeId = id,
                            head,
                            state,
                        }
                    );
                }
            default:
                throw Invalid("Unknown change action.");
        }
    }

    private async Task<JsonObject> ReadChangeAsync(JsonObject arguments, CancellationToken token)
    {
        Fields(arguments, ["id"]);
        long id = Id(arguments, "id");
        JsonObject change = Change(id);
        string head = change["head"]!.GetValue<string>();
        change["diff"] = await GitAsync(
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
            )
            .ConfigureAwait(false);
        using SqliteConnection connection = database.Open();
        change["integrationRequests"] = Requests(connection, id);
        return Operation.Success(change);
    }

    private JsonObject ListChanges(JsonObject arguments)
    {
        Fields(arguments, ["state", "cursor", "limit"]);
        string? state = OptionalText(arguments, "state");
        if (state is not null and not ("open" or "integrated"))
        {
            throw Invalid("Unknown change state.");
        }

        (long cursor, int limit) = Page(arguments);
        List<long> ids = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                """
                SELECT id FROM changes WHERE id>$cursor AND ($state IS NULL OR state=$state)
                ORDER BY id LIMIT $limit;
                """,
                null,
                ("$cursor", cursor),
                ("$state", state),
                ("$limit", limit + 1)
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        bool more = ids.Count > limit;
        JsonArray items = [];
        foreach (long id in ids.Take(limit))
        {
            items.Add(Change(id));
        }

        return Operation.Success(new { items, nextCursor = more ? (long?)ids[limit - 1] : null });
    }

    private JsonObject Review(string actor, JsonObject arguments)
    {
        RequireInitialized();
        Fields(arguments, ["id", "head", "decision", "body"]);
        long changeId = Id(arguments, "id");
        string head = Text(arguments, "head");
        string decision = Text(arguments, "decision");
        string body = Text(arguments, "body");
        if (decision is not ("approve" or "request-changes" or "comment"))
        {
            throw Invalid("Unknown review decision.");
        }

        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        RequireWritable(connection, transaction);
        JsonObject change = Change(connection, transaction, changeId);
        RequireOpen(change);
        if (change["head"]!.GetValue<string>() != head)
        {
            throw new OperationException(
                "stale_candidate",
                "Review must identify the exact current change head."
            );
        }

        if (decision != "comment")
        {
            RequireNotRunning(connection, transaction, changeId);
        }

        Sql.Execute(
            connection,
            """
            INSERT INTO reviews(change_id,actor,head,decision,body,created_at)
            VALUES($id,$actor,$head,$decision,$body,$time);
            """,
            transaction,
            ("$id", changeId),
            ("$actor", actor),
            ("$head", head),
            ("$decision", decision),
            ("$body", body),
            ("$time", Stamp)
        );
        long id = LastId(connection, transaction);
        string createdAt = Stamp;
        Event(
            connection,
            transaction,
            "review",
            $"{actor} recorded {decision} on change {changeId} at {head}."
        );
        transaction.Commit();
        return Operation.Success(
            new
            {
                id,
                changeId,
                actor,
                head,
                decision,
                body,
                createdAt,
            }
        );
    }

    private JsonObject ReadChecks(JsonObject arguments)
    {
        Fields(arguments, ["id", "changeId"]);
        if (arguments.ContainsKey("id") == arguments.ContainsKey("changeId"))
        {
            throw Invalid("Exactly one of id or changeId is required.");
        }

        long id = Id(arguments, arguments.ContainsKey("id") ? "id" : "changeId");
        using SqliteConnection connection = database.Open();
        if (arguments.ContainsKey("changeId"))
        {
            _ = Change(connection, null, id);
        }

        JsonArray items = CheckRows(
            connection,
            null,
            arguments.ContainsKey("id") ? "id=$id" : "change_id=$id",
            id
        );
        if (arguments.ContainsKey("id") && items.Count == 0)
        {
            throw new OperationException("not_found", "Check does not exist.");
        }

        return Operation.Success(new { items });
    }

    private async Task IntegrateAsync(long requestId, CancellationToken token)
    {
        long changeId;
        string submitted;
        using (SqliteConnection connection = database.Open())
        using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
        {
            using (
                SqliteCommand command = Sql.Command(
                    connection,
                    "SELECT change_id,head FROM integration_requests WHERE id=$id AND "
                        + "state='pending';",
                    transaction,
                    ("$id", requestId)
                )
            )
            using (ObservedSqliteReader reader = Sql.Reader(command))
            {
                if (!reader.Read())
                {
                    return;
                }

                changeId = reader.GetInt64(0);
                submitted = reader.GetString(1);
            }

            Sql.Execute(
                connection,
                "UPDATE integration_requests SET state='running',updated_at=$time WHERE id=$id;",
                transaction,
                ("$time", Stamp),
                ("$id", requestId)
            );
            Event(
                connection,
                transaction,
                "integration-started",
                $"Integration request {requestId} started."
            );
            transaction.Commit();
        }

        JsonObject change = Change(changeId);
        RequireOpen(change);
        await RequireSubmittedHeadAsync(change, submitted, token).ConfigureAwait(false);
        EnforceHealth(change);
        await VerifyWorktreeAsync(IntegrationBranch, IntegrationPath, token).ConfigureAwait(false);
        await RequireCleanAsync(IntegrationPath, token).ConfigureAwait(false);
        string parent = await ResolveAsync(IntegrationBranch, token).ConfigureAwait(false);
        await RequireReviewsAsync(change, parent, token).ConfigureAwait(false);
        string candidateBranch = $"refs/heads/lite-agensh/{database.RunId}/candidate-{requestId}";
        string path = Path.Combine(
            database.StatePath,
            "worktrees",
            "candidate-" + requestId.ToString(CultureInfo.InvariantCulture)
        );
        await EnsureWorktreeAsync(candidateBranch, path, parent, token).ConfigureAwait(false);
        ProcessResult merge = await GitResultAsync(
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

        using (SqliteConnection connection = database.Open())
        {
            Sql.Execute(
                connection,
                "UPDATE integration_requests SET "
                    + "parent_commit=$parent,candidate_commit=$candidate WHERE id=$id;",
                null,
                ("$parent", parent),
                ("$candidate", candidate),
                ("$id", requestId)
            );
        }

        bool passed = await RunChecksAsync(
                changeId,
                "candidate",
                candidate,
                path,
                database.Configuration.CandidateChecks,
                "candidate-" + requestId.ToString(CultureInfo.InvariantCulture),
                token
            )
            .ConfigureAwait(false);
        if (!passed)
        {
            throw new OperationException(
                "check_failed",
                $"Candidate checks failed; work and logs are retained at {path}."
            );
        }

        await RequireExactTreeAsync(path, candidate, token).ConfigureAwait(false);
        await RequireSubmittedHeadAsync(Change(changeId), submitted, token).ConfigureAwait(false);
        await RequireReviewsAsync(Change(changeId), parent, token).ConfigureAwait(false);
        EnforceHealth(Change(changeId));
        if (
            await ResolveAsync(IntegrationBranch, token).ConfigureAwait(false) != parent
            || await ResolveAsync(candidateBranch, token).ConfigureAwait(false) != candidate
        )
        {
            throw new OperationException(
                "stale_candidate",
                "Integration or candidate moved during checks."
            );
        }

        await RequireExactTreeAsync(IntegrationPath, parent, token).ConfigureAwait(false);
        await RequireSubmittedHeadAsync(Change(changeId), submitted, token).ConfigureAwait(false);
        if (await ResolveAsync(candidateBranch, token).ConfigureAwait(false) != candidate)
        {
            throw new OperationException(
                "stale_candidate",
                "Candidate ref changed before integration advance."
            );
        }

        using (SqliteConnection connection = database.Open())
        {
            // The durable intent permits recovery across the Git/SQLite commit boundary.
            Sql.Execute(
                connection,
                "UPDATE integration_requests SET ready=1 WHERE id=$id;",
                null,
                ("$id", requestId)
            );
        }

        token.ThrowIfCancellationRequested();
        await GitAsync(
                Repository,
                ["update-ref", IntegrationBranch, candidate, parent],
                CancellationToken.None
            )
            .ConfigureAwait(false);
        await GitAsync(IntegrationPath, ["reset", "--hard", candidate], CancellationToken.None)
            .ConfigureAwait(false);
        FinishRequest(
            requestId,
            true,
            "ok",
            "Exact checked merged candidate integrated.",
            candidate
        );
        await EvaluateHealthCoreAsync(forceChecks: true, token).ConfigureAwait(false);
    }

    private async Task<bool> TryRecoverAdvanceAsync(long id)
    {
        string? candidate;
        using (SqliteConnection connection = database.Open())
        {
            candidate =
                Sql.Scalar(
                    connection,
                    "SELECT candidate_commit FROM integration_requests WHERE id=$id AND "
                        + "state='running' AND ready=1;",
                    null,
                    ("$id", id)
                ) as string;
        }

        if (
            candidate is null
            || await ResolveAsync(IntegrationBranch, CancellationToken.None).ConfigureAwait(false)
                != candidate
        )
        {
            return false;
        }

        await RecoverRequestsAsync(CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    private async Task RecoverRequestsAsync(CancellationToken token)
    {
        List<(long Id, string? Parent, string? Candidate, bool Ready)> requests = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id,parent_commit,candidate_commit,ready FROM "
                    + "integration_requests WHERE state='running' ORDER BY id;"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                requests.Add(
                    (
                        reader.GetInt64(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetBoolean(3)
                    )
                );
            }
        }

        foreach ((long id, string? parent, string? candidate, bool ready) in requests)
        {
            string current = await ResolveAsync(IntegrationBranch, token).ConfigureAwait(false);
            if (ready && candidate is not null && current == candidate)
            {
                await VerifyWorktreeAsync(IntegrationBranch, IntegrationPath, token)
                    .ConfigureAwait(false);
                string tree = (
                    await GitAsync(Repository, ["rev-parse", candidate + "^{tree}"], token)
                        .ConfigureAwait(false)
                ).Trim();
                string index = (
                    await GitAsync(IntegrationPath, ["write-tree"], token).ConfigureAwait(false)
                ).Trim();
                string? previousTree = parent is null
                    ? null
                    : (
                        await GitAsync(Repository, ["rev-parse", parent + "^{tree}"], token)
                            .ConfigureAwait(false)
                    ).Trim();
                if (
                    (index != tree && index != previousTree)
                    || (
                        await GitResultAsync(IntegrationPath, ["diff", "--quiet", "--"], token)
                            .ConfigureAwait(false)
                    ).ExitCode != 0
                    || (
                        await GitAsync(
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

                await GitAsync(IntegrationPath, ["reset", "--hard", candidate], token)
                    .ConfigureAwait(false);
                FinishRequest(
                    id,
                    true,
                    "ok",
                    "Recovered the durable checked integration advance.",
                    candidate
                );
            }
            else
            {
                FinishRequest(
                    id,
                    false,
                    "stale_candidate",
                    "Interrupted integration did not prove a checked advance; candidate "
                        + "and worker work are retained.",
                    null
                );
            }
        }
    }

    private void FinishRequest(long id, bool succeeded, string code, string message, string? commit)
    {
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        object? changeId = Sql.Scalar(
            connection,
            "SELECT change_id FROM integration_requests WHERE id=$id AND state IN "
                + "('running','pending');",
            transaction,
            ("$id", id)
        );
        if (changeId is null)
        {
            return;
        }

        Sql.Execute(
            connection,
            """
            UPDATE integration_requests SET state=$state,result_code=$code,message=$message,
                integrated_commit=$commit,updated_at=$time
            WHERE id=$id;
            """,
            transaction,
            ("$state", succeeded ? "succeeded" : "failed"),
            ("$code", code),
            ("$message", message),
            ("$commit", commit),
            ("$time", Stamp),
            ("$id", id)
        );
        if (succeeded)
        {
            Sql.Execute(
                connection,
                "UPDATE changes SET state='integrated',updated_at=$time WHERE id=$id;",
                transaction,
                ("$time", Stamp),
                ("$id", changeId)
            );
            string? author =
                Sql.Scalar(
                    connection,
                    "SELECT author FROM changes WHERE id=$id;",
                    transaction,
                    ("$id", changeId)
                ) as string;
            if (database.Configuration.PlanningSeconds > 0 && author == "worker1")
            {
                SetMeta(connection, transaction, "git:plan_integrated", "true");
            }
        }

        Event(
            connection,
            transaction,
            "integration-result",
            $"Integration {id} {(succeeded ? "succeeded" : "failed")} ({code}): {message}"
        );
        transaction.Commit();
    }

    private async Task<JsonObject> EvaluateHealthCoreAsync(
        bool forceChecks,
        CancellationToken token
    )
    {
        RequireSettledProcessOwnership();
        string head = await ResolveAsync(IntegrationBranch, token).ConfigureAwait(false);
        JsonObject? old = ReadHealth();
        bool passed;
        if (forceChecks || old is null || old["commit"]!.GetValue<string>() != head)
        {
            await VerifyWorktreeAsync(IntegrationBranch, IntegrationPath, token)
                .ConfigureAwait(false);
            await RequireCleanAsync(IntegrationPath, token).ConfigureAwait(false);
            passed = await RunChecksAsync(
                    null,
                    "integration",
                    head,
                    IntegrationPath,
                    database.Configuration.IntegrationChecks,
                    Guid.NewGuid().ToString("N"),
                    token
                )
                .ConfigureAwait(false);
            await RequireExactTreeAsync(IntegrationPath, head, token).ConfigureAwait(false);
        }
        else
        {
            passed = old["passed"]!.GetValue<bool>();
        }

        if (await ResolveAsync(IntegrationBranch, token).ConfigureAwait(false) != head)
        {
            throw new OperationException(
                "stale_candidate",
                "Integration moved during health evaluation."
            );
        }

        DateTimeOffset? redSince = old?["redSince"] is JsonValue since
            ? DateTimeOffset.Parse(since.GetValue<string>(), CultureInfo.InvariantCulture)
            : null;
        bool alerted = old?["teamAlerted"]?.GetValue<bool>() ?? false;
        bool lifted = old?["restrictionLifted"]?.GetValue<bool>() ?? false;
        string broken = old?["brokenCommit"]?.GetValue<string>() ?? "";
        List<(string Kind, string Message, string? Recipient)> events = [];
        if (!passed)
        {
            if (redSince is null)
            {
                redSince = database.Now;
                broken = head;
                alerted = lifted = false;
                events.Add(
                    (
                        "health-broken",
                        $"Integration fails at {head}; only changes linked to "
                            + $"integration-health task {EnsureHealthTask()} may integrate.",
                        BreakingAuthor(head)
                    )
                );
            }
            else if (!alerted && database.Now - redSince.Value >= TimeSpan.FromMinutes(15))
            {
                alerted = true;
                events.Add(
                    (
                        "health-alert",
                        "Integration has failed for at least 15 minutes. All workers "
                            + "should help restore integration health.",
                        null
                    )
                );
            }

            if (!lifted && database.Now - redSince.Value >= TimeSpan.FromMinutes(60))
            {
                lifted = true;
                events.Add(
                    (
                        "health-lift",
                        "Integration still fails; the fix-main restriction lifted after "
                            + "60 minutes. Required candidate/final checks still apply.",
                        null
                    )
                );
            }
        }
        else if (redSince is not null)
        {
            events.Add(("health-recovered", $"Integration passes again at {head}.", null));
            redSince = null;
            broken = "";
            alerted = lifted = false;
        }

        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection,
            """
            INSERT INTO git_health(singleton,commit_hash,passed,red_since,broken_commit,
                alerted,lifted,checked_at)
            VALUES(1,$head,$passed,$since,$broken,$alerted,$lifted,$time)
            ON CONFLICT(singleton) DO UPDATE SET commit_hash=excluded.commit_hash,
            passed=excluded.passed,red_since=excluded.red_since,
            broken_commit=excluded.broken_commit,alerted=excluded.alerted,
            lifted=excluded.lifted,checked_at=excluded.checked_at;
            """,
            transaction,
            ("$head", head),
            ("$passed", passed),
            ("$since", redSince?.ToString("O", CultureInfo.InvariantCulture)),
            ("$broken", broken),
            ("$alerted", alerted),
            ("$lifted", lifted),
            ("$time", Stamp)
        );
        foreach ((string kind, string message, string? recipient) in events)
        {
            Event(connection, transaction, kind, message, recipient);
        }

        transaction.Commit();
        return ReadHealth()!;
    }

    private string? BreakingAuthor(string commit)
    {
        using SqliteConnection connection = database.Open();
        return Sql.Scalar(
                connection,
                """
                SELECT c.author FROM changes c JOIN integration_requests r ON c.id=r.change_id
                WHERE r.state='succeeded' AND r.integrated_commit=$commit
                ORDER BY r.id DESC LIMIT 1;
                """,
                null,
                ("$commit", commit)
            ) as string;
    }

    private JsonObject? ReadHealth()
    {
        using SqliteConnection connection = database.Open();
        using SqliteCommand command = Sql.Command(
            connection,
            "SELECT commit_hash,passed,red_since,broken_commit,alerted,lifted,checked_at "
                + "FROM git_health WHERE singleton=1;"
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        return !reader.Read()
            ? null
            : new JsonObject
            {
                ["commit"] = reader.GetString(0),
                ["passed"] = reader.GetBoolean(1),
                ["redSince"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                ["brokenCommit"] = reader.GetString(3),
                ["teamAlerted"] = reader.GetBoolean(4),
                ["restrictionLifted"] = reader.GetBoolean(5),
                ["fixMainOnly"] = !reader.GetBoolean(1) && !reader.GetBoolean(5),
                ["checkedAt"] = reader.GetString(6),
                ["taskId"] = long.TryParse(
                    database.ReadMetadata("git:health_task"),
                    CultureInfo.InvariantCulture,
                    out long task
                )
                    ? task
                    : null,
            };
    }

    private void EnforceHealth(JsonObject change)
    {
        JsonObject? health = ReadHealth();
        if (
            health?["fixMainOnly"]?.GetValue<bool>() == true
            && !change["taskIds"]!
                .AsArray()
                .Any(node => node!.GetValue<long>() == EnsureHealthTask())
        )
        {
            throw new OperationException(
                "check_failed",
                "Integration is failing; only changes linked to the dedicated "
                    + "integration-health task may integrate before the 60-minute lift."
            );
        }
    }

    private long EnsureHealthTask()
    {
        if (
            long.TryParse(
                database.ReadMetadata("git:health_task"),
                CultureInfo.InvariantCulture,
                out long existing
            )
        )
        {
            return existing;
        }

        string title = "Integration health (fix-main) " + database.RunId;
        using (SqliteConnection connection = database.Open())
        {
            object? recovered = Sql.Scalar(
                connection,
                "SELECT id FROM tasks WHERE title=$title AND author='owner' ORDER BY id LIMIT 1;",
                null,
                ("$title", title)
            );
            if (recovered is not null)
            {
                long recoveredId = Convert.ToInt64(recovered, CultureInfo.InvariantCulture);
                database.WriteMetadata(
                    "git:health_task",
                    recoveredId.ToString(CultureInfo.InvariantCulture)
                );
                return recoveredId;
            }
        }

        JsonObject result = collaboration.Invoke(
            "owner",
            "task_write",
            new JsonObject
            {
                ["action"] = "create",
                ["title"] = title,
                ["body"] =
                    "Restore the integration branch's declared checks. Link fix-main "
                        + "submissions to this task. Candidate and final checks remain "
                        + "required.",
            }
        );
        if (result["ok"]?.GetValue<bool>() != true || result["data"]?["id"] is not JsonValue id)
        {
            throw new OperationException(
                "unavailable",
                "Could not create the dedicated integration-health task."
            );
        }

        long value = id.GetValue<long>();
        database.WriteMetadata("git:health_task", value.ToString(CultureInfo.InvariantCulture));
        return value;
    }

    private async Task<bool> RunChecksAsync(
        long? changeId,
        string kind,
        string commit,
        string path,
        CheckCommand[] commands,
        string batch,
        CancellationToken token
    )
    {
        string tree = (
            await GitAsync(Repository, ["rev-parse", commit + "^{tree}"], token)
                .ConfigureAwait(false)
        ).Trim();
        bool passed = true;
        foreach (CheckCommand command in commands)
        {
            token.ThrowIfCancellationRequested();
            RequireSettledProcessOwnership();
            await RequireExactTreeAsync(path, commit, token).ConfigureAwait(false);
            string status;
            int? exit = null;
            string log;
            bool cancelled = false;
            try
            {
                ProcessResult result = await processes
                    .RunAsync(
                        command.Executable,
                        command.Arguments,
                        path,
                        TimeSpan.FromSeconds(command.TimeoutSeconds),
                        token
                    )
                    .ConfigureAwait(false);
                exit = result.ExitCode;
                status =
                    result.TimedOut ? "timed-out"
                    : result.ExitCode == 0 ? "passed"
                    : "failed";
                log = $"stdout:\n{result.StandardOutput}\nstderr:\n{result.StandardError}";
                try
                {
                    await RequireExactTreeAsync(path, commit, token).ConfigureAwait(false);
                }
                catch (OperationException exception)
                {
                    status = "failed";
                    log += "\nCandidate identity failure: " + exception.Message;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                status = "cancelled";
                log =
                    "Caller cancelled the check; ProcessRunner settled owned execution "
                        + "before returning.";
                cancelled = true;
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                status = "failed";
                log = exception.ToString();
            }

            Directory.CreateDirectory(Path.Combine(database.StatePath, "logs", "checks"));
            string logPath = Path.Combine(
                database.StatePath,
                "logs",
                "checks",
                Guid.NewGuid().ToString("N") + ".log"
            );
            await File.WriteAllTextAsync(logPath, log, Encoding.UTF8, CancellationToken.None)
                .ConfigureAwait(false);
            using (SqliteConnection connection = database.Open())
            using (SqliteTransactionScope transaction = Sql.BeginTransaction(connection))
            {
                Sql.Execute(
                    connection,
                    """
                    INSERT INTO checks(change_id,kind,commit_hash,tree,command_json,status,
                        exit_code,log_path,created_at,batch)
                    VALUES($change,$kind,$commit,$tree,$command,$status,$exit,$log,$time,$batch);
                    """,
                    transaction,
                    ("$change", changeId),
                    ("$kind", kind),
                    ("$commit", commit),
                    ("$tree", tree),
                    ("$command", JsonSerializer.Serialize(command, Json.Options)),
                    ("$status", status),
                    ("$exit", exit),
                    ("$log", logPath),
                    ("$time", Stamp),
                    ("$batch", batch)
                );
                Event(
                    connection,
                    transaction,
                    "check",
                    $"{kind} check {LastId(connection, transaction)} on {commit}: "
                        + $"{status}; log {logPath}."
                );
                transaction.Commit();
            }

            passed &= status == "passed";
            if (cancelled)
            {
                token.ThrowIfCancellationRequested();
            }

            RequireSettledProcessOwnership();
        }

        return passed;
    }

    private async Task RequireReviewsAsync(
        JsonObject change,
        string parent,
        CancellationToken token
    )
    {
        string head = change["head"]!.GetValue<string>();
        string author = change["author"]!.GetValue<string>();
        Dictionary<string, string> latest = new(StringComparer.Ordinal);
        JsonArray activeReviews = [];
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT actor,head,decision FROM reviews WHERE change_id=$id AND stale=0 "
                    + "ORDER BY id;",
                null,
                ("$id", change["id"]!.GetValue<long>())
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                activeReviews.Add(
                    new JsonObject
                    {
                        ["actor"] = reader.GetString(0),
                        ["head"] = reader.GetString(1),
                        ["decision"] = reader.GetString(2),
                    }
                );
            }
        }

        foreach (JsonNode? item in activeReviews)
        {
            JsonObject review = item!.AsObject();
            string actor = review["actor"]!.GetValue<string>();
            string decision = review["decision"]!.GetValue<string>();
            if (
                review["head"]!.GetValue<string>() == head
                && actor != author
                && decision != "comment"
            )
            {
                latest[actor] = decision;
            }
        }

        if (
            database.Configuration.RequirePeerReview
            && !latest.Any(pair => pair.Key != "owner" && pair.Value == "approve")
        )
        {
            throw new OperationException(
                "review_required",
                "A different worker must approve the exact submitted head."
            );
        }

        string files = await GitAsync(
                Repository,
                ["diff", "--no-renames", "--name-only", "-z", parent + "..." + head, "--"],
                token
            )
            .ConfigureAwait(false);
        bool protectedChange = files
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Any(file =>
                database.Configuration.ProtectedPaths.Any(pattern => MatchesPath(file, pattern))
            );
        if (
            protectedChange
            && (
                !latest.TryGetValue("owner", out string? ownerDecision)
                || ownerDecision != "approve"
            )
        )
        {
            throw new OperationException(
                "review_required",
                "Protected paths require owner approval of this exact head."
            );
        }
    }

    private async Task RequireSubmittedHeadAsync(
        JsonObject change,
        string head,
        CancellationToken token
    )
    {
        if (
            change["head"]!.GetValue<string>() != head
            || await ResolveAsync(change["branch"]!.GetValue<string>(), token).ConfigureAwait(false)
                != head
        )
        {
            throw new OperationException(
                "stale_candidate",
                "The submitted worker head changed; update and review the new head."
            );
        }
    }

    private async Task EnsureWorktreeAsync(
        string reference,
        string path,
        string commit,
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
                        await GitResultAsync(
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
            await VerifyWorktreeAsync(reference, path, token).ConfigureAwait(false);
            return;
        }

        ProcessResult exists = await GitResultAsync(
                Repository,
                ["show-ref", "--verify", "--quiet", reference],
                token
            )
            .ConfigureAwait(false);
        if (exists.ExitCode != 0)
        {
            await GitAsync(
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

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!Directory.Exists(path))
        {
            await GitAsync(
                    Repository,
                    ["worktree", "add", "--quiet", path, reference["refs/heads/".Length..]],
                    token
                )
                .ConfigureAwait(false);
        }

        await VerifyWorktreeAsync(reference, path, token).ConfigureAwait(false);
        using SqliteConnection store = database.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(store);
        Sql.Execute(
            store,
            "UPDATE git_resources SET state='ready' WHERE ref=$ref;",
            transaction,
            ("$ref", reference)
        );
        Event(store, transaction, "worktree", $"Allocated owned worktree {path} on {reference}.");
        transaction.Commit();
    }

    private async Task VerifyWorktreeAsync(string reference, string path, CancellationToken token)
    {
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
                saved is null
                || !SamePath(path, saved)
                || !Path.GetFullPath(path)
                    .StartsWith(
                        Path.Combine(database.StatePath, "worktrees") + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    )
                || SamePath(path, Repository)
            )
            {
                throw new OperationException(
                    "conflict",
                    "Worktree is not an identified owned resource."
                );
            }
        }

        string root = (
            await GitAsync(path, ["rev-parse", "--show-toplevel"], token).ConfigureAwait(false)
        ).Trim();
        string branch = (
            await GitAsync(path, ["symbolic-ref", "-q", "HEAD"], token).ConfigureAwait(false)
        ).Trim();
        string common = (
            await GitAsync(path, ["rev-parse", "--path-format=absolute", "--git-common-dir"], token)
                .ConfigureAwait(false)
        ).Trim();
        string repositoryCommon = (
            await GitAsync(
                    Repository,
                    ["rev-parse", "--path-format=absolute", "--git-common-dir"],
                    token
                )
                .ConfigureAwait(false)
        ).Trim();
        if (!SamePath(root, path) || branch != reference || !SamePath(common, repositoryCommon))
        {
            throw new OperationException(
                "conflict",
                "Worktree identity does not match the saved run association."
            );
        }
    }

    private async Task RequireCleanAsync(string path, CancellationToken token)
    {
        string status = await GitAsync(
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

    private async Task RequireExactTreeAsync(string path, string commit, CancellationToken token)
    {
        string current = (
            await GitAsync(path, ["rev-parse", "--verify", "HEAD"], token).ConfigureAwait(false)
        ).Trim();
        ProcessResult diff = await GitResultAsync(path, ["diff", "--quiet", "HEAD", "--"], token)
            .ConfigureAwait(false);
        ProcessResult index = await GitResultAsync(
                path,
                ["diff", "--quiet", "--cached", "HEAD", "--"],
                token
            )
            .ConfigureAwait(false);
        if (current != commit || diff.ExitCode != 0 || index.ExitCode != 0)
        {
            throw new OperationException(
                "stale_candidate",
                "Checked commit or tracked tree/index changed; old check evidence cannot "
                    + "advance integration."
            );
        }
    }

    private async Task RetainDeliveryAsync(string head, CancellationToken token)
    {
        string worktrees = await GitAsync(
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

        ProcessResult existing = await GitResultAsync(
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
                await GitResultAsync(
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

        await GitAsync(Repository, ["update-ref", DeliveryBranch, head, expected], token)
            .ConfigureAwait(false);
        using SqliteConnection store = database.Open();
        Sql.Execute(
            store,
            "UPDATE git_resources SET state='ready' WHERE ref=$ref;",
            null,
            ("$ref", DeliveryBranch)
        );
    }

    private JsonObject BuildDeliveryReport(string head, string batch, bool passed)
    {
        using SqliteConnection connection = database.Open();
        JsonArray unresolvedChanges = [];
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id,title,author,head FROM changes WHERE state='open' ORDER BY id;"
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                unresolvedChanges.Add(
                    new JsonObject
                    {
                        ["id"] = reader.GetInt64(0),
                        ["title"] = reader.GetString(1),
                        ["author"] = reader.GetString(2),
                        ["head"] = reader.GetString(3),
                    }
                );
            }
        }

        JsonArray tasks = [];
        long cursor = 0;
        while (true)
        {
            JsonObject result = collaboration.Invoke(
                "owner",
                "task_list",
                new JsonObject
                {
                    ["state"] = "open",
                    ["cursor"] = cursor,
                    ["limit"] = 200,
                }
            );
            if (result["ok"]?.GetValue<bool>() != true)
            {
                throw new OperationException(
                    "unavailable",
                    "Unresolved task inspection failed; delivery report is incomplete."
                );
            }

            JsonObject page = result["data"]!.AsObject();
            foreach (JsonNode? item in page["items"]!.AsArray())
            {
                tasks.Add(item!.DeepClone());
            }

            if (page["nextCursor"] is not JsonValue next)
            {
                break;
            }

            long nextCursor = next.GetValue<long>();
            if (nextCursor <= cursor)
            {
                throw new InvalidOperationException("Task pagination did not advance.");
            }

            cursor = nextCursor;
        }

        using SqliteCommand checksCommand = Sql.Command(
            connection,
            "SELECT id FROM checks ORDER BY id;"
        );
        List<long> checkIds = [];
        using (ObservedSqliteReader reader = Sql.Reader(checksCommand))
        {
            while (reader.Read())
            {
                checkIds.Add(reader.GetInt64(0));
            }
        }

        JsonArray checks = [];
        foreach (long id in checkIds)
        {
            checks.Add(CheckRows(connection, null, "id=$id", id)[0]!.DeepClone());
        }

        return new JsonObject
        {
            ["baseCommit"] = database.Configuration.BaseRevision,
            ["finalCommit"] = head,
            ["deliveryRef"] = DeliveryBranch,
            ["accepted"] = passed,
            ["checks"] = checks,
            ["finalCheckBatch"] = batch,
            ["integrationHealth"] = ReadHealth(),
            ["unresolvedTasks"] = tasks,
            ["unresolvedChanges"] = unresolvedChanges,
            ["statePath"] = database.StatePath,
            ["createdAt"] = Stamp,
        };
    }

    private JsonObject Change(long id)
    {
        using SqliteConnection connection = database.Open();
        return Change(connection, null, id);
    }

    private static JsonObject Change(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long id
    )
    {
        JsonObject change;
        using (
            SqliteCommand command = Sql.Command(
                connection,
                """
                SELECT id,author,branch,head,title,body,task_ids,state,created_at,updated_at
                FROM changes WHERE id=$id;
                """,
                transaction,
                ("$id", id)
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            if (!reader.Read())
            {
                throw new OperationException("not_found", "Change does not exist.");
            }

            change = new JsonObject
            {
                ["id"] = reader.GetInt64(0),
                ["author"] = reader.GetString(1),
                ["branch"] = reader.GetString(2),
                ["head"] = reader.GetString(3),
                ["title"] = reader.GetString(4),
                ["body"] = reader.GetString(5),
                ["taskIds"] = JsonNode.Parse(reader.GetString(6)),
                ["state"] = reader.GetString(7),
                ["createdAt"] = reader.GetString(8),
                ["updatedAt"] = reader.GetString(9),
            };
        }

        JsonArray comments = [];
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id,actor,body,created_at FROM change_comments WHERE "
                    + "change_id=$id ORDER BY id;",
                transaction,
                ("$id", id)
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                comments.Add(
                    new JsonObject
                    {
                        ["id"] = reader.GetInt64(0),
                        ["actor"] = reader.GetString(1),
                        ["body"] = reader.GetString(2),
                        ["createdAt"] = reader.GetString(3),
                    }
                );
            }
        }

        JsonArray reviews = [];
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id,actor,head,decision,body,created_at FROM reviews WHERE "
                    + "change_id=$id ORDER BY id;",
                transaction,
                ("$id", id)
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                reviews.Add(
                    new JsonObject
                    {
                        ["id"] = reader.GetInt64(0),
                        ["changeId"] = id,
                        ["actor"] = reader.GetString(1),
                        ["head"] = reader.GetString(2),
                        ["decision"] = reader.GetString(3),
                        ["body"] = reader.GetString(4),
                        ["createdAt"] = reader.GetString(5),
                    }
                );
            }
        }

        change["comments"] = comments;
        change["reviews"] = reviews;
        change["checks"] = CheckRows(connection, transaction, "change_id=$id", id);
        return change;
    }

    private static JsonArray CheckRows(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string predicate,
        long id
    )
    {
        JsonArray items = [];
        using SqliteCommand command = Sql.Command(
            connection,
            $"""
            SELECT id,change_id,kind,commit_hash,tree,command_json,status,exit_code,log_path,
                created_at
            FROM checks WHERE {predicate} ORDER BY id;
            """,
            transaction,
            ("$id", id)
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        while (reader.Read())
        {
            items.Add(
                new JsonObject
                {
                    ["id"] = reader.GetInt64(0),
                    ["changeId"] = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    ["kind"] = reader.GetString(2),
                    ["commit"] = reader.GetString(3),
                    ["tree"] = reader.GetString(4),
                    ["command"] = JsonNode.Parse(reader.GetString(5)),
                    ["status"] = reader.GetString(6),
                    ["exitCode"] = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    ["logPath"] = reader.GetString(8),
                    ["createdAt"] = reader.GetString(9),
                }
            );
        }

        return items;
    }

    private static JsonArray Requests(SqliteConnection connection, long id)
    {
        JsonArray items = [];
        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT id,actor,head,state,result_code,message,integrated_commit,created_at,updated_at
            FROM integration_requests WHERE change_id=$id ORDER BY id;
            """,
            null,
            ("$id", id)
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        while (reader.Read())
        {
            items.Add(
                new JsonObject
                {
                    ["requestId"] = reader.GetInt64(0),
                    ["changeId"] = id,
                    ["actor"] = reader.GetString(1),
                    ["head"] = reader.GetString(2),
                    ["state"] = reader.GetString(3),
                    ["resultCode"] = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ["message"] = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ["integratedCommit"] = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ["createdAt"] = reader.GetString(7),
                    ["updatedAt"] = reader.GetString(8),
                }
            );
        }

        return items;
    }

    private string WorkerBranch(string worker)
    {
        _ = GetWorkerPath(worker);
        return $"refs/heads/lite-agensh/{database.RunId}/{worker}";
    }

    private FileStream AcquireOwnership()
    {
        try
        {
            return new FileStream(
                Path.Combine(database.StatePath, "git-coordinator.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
        }
        catch (IOException exception)
        {
            throw new OperationException(
                "conflict",
                "Another Git coordinator operation owns this run: " + exception.Message
            );
        }
    }

    private void RequireInitialized()
    {
        if (
            database.ReadMetadata("git:initialized") != "true"
            || database.ReadMetadata("git:cleaned") == "true"
            || database.ReadMetadata("git:cleaning") == "true"
        )
        {
            throw new OperationException(
                "unavailable",
                "Git worktrees are not initialized or have been cleaned up."
            );
        }
    }

    private void RequireQuiescent(bool allowFinalizing)
    {
        string? lifecycle = database.ReadMetadata("lifecycle");
        if (
            lifecycle is not ("paused" or "finalized" or "stopped")
            && !(allowFinalizing && lifecycle == "finalizing")
        )
        {
            throw new OperationException(
                "conflict",
                "Quiescent coordinator lifecycle is required."
            );
        }

        using SqliteConnection connection = database.Open();
        if (
            Convert.ToInt64(
                Sql.Scalar(
                    connection,
                    "SELECT COUNT(*) FROM process_leases WHERE state<>'exited';"
                ),
                CultureInfo.InvariantCulture
            ) != 0
        )
        {
            throw new OperationException(
                "conflict",
                "Unsettled owned processes block finalization and cleanup."
            );
        }
    }

    private void RequireSettledProcessOwnership()
    {
        using SqliteConnection connection = database.Open();
        if (
            Sql.Scalar(
                connection,
                "SELECT id FROM process_leases WHERE state='unresolved' LIMIT 1;"
            )
            is not null
        )
        {
            throw new OperationException(
                "unavailable",
                "An unresolved owned process blocks further coordinator execution."
            );
        }
    }

    private static void RequireWritable(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (
            Sql.Scalar(connection, "SELECT value FROM meta WHERE key='git:cleaned';", transaction)
                as string
                == "true"
            || Sql.Scalar(
                connection,
                "SELECT value FROM meta WHERE key='git:cleaning';",
                transaction
            ) as string
                == "true"
        )
        {
            throw new OperationException(
                "conflict",
                "Explicit cleanup has frozen Git domain mutations."
            );
        }
    }

    private async Task<string> ResolveAsync(string revision, CancellationToken token) =>
        (
            await GitAsync(
                    Repository,
                    ["rev-parse", "--verify", "--end-of-options", revision + "^{commit}"],
                    token
                )
                .ConfigureAwait(false)
        ).Trim();

    private async Task<ProcessResult> GitResultAsync(
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken token
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
            .RunAsync("git", [.. options, .. arguments], path, TimeSpan.FromSeconds(60), token)
            .ConfigureAwait(false);
    }

    private async Task<string> GitAsync(
        string path,
        IReadOnlyList<string> arguments,
        CancellationToken token
    )
    {
        ProcessResult result = await GitResultAsync(path, arguments, token).ConfigureAwait(false);
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

    private void Event(
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

    private static void SetMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value
    ) =>
        Sql.Execute(
            connection,
            "INSERT INTO meta(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE "
                + "SET value=excluded.value;",
            transaction,
            ("$key", key),
            ("$value", value)
        );

    private static long LastId(SqliteConnection connection, SqliteTransaction transaction) =>
        Convert.ToInt64(
            Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction),
            CultureInfo.InvariantCulture
        );

    private static void RequireAuthor(JsonObject change, string actor)
    {
        if (change["author"]!.GetValue<string>() != actor)
        {
            throw new OperationException("not_visible", "Only the change author may update it.");
        }
    }

    private static void RequireOpen(JsonObject change)
    {
        if (change["state"]!.GetValue<string>() != "open")
        {
            throw new OperationException("conflict", "Change has already been integrated.");
        }
    }

    private static void RequireNotRunning(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long id
    )
    {
        if (
            Sql.Scalar(
                connection,
                "SELECT id FROM integration_requests WHERE change_id=$id AND state='running';",
                transaction,
                ("$id", id)
            )
            is not null
        )
        {
            throw new OperationException(
                "conflict",
                "Candidate identity/reviews are frozen while the coordinator integrates it."
            );
        }
    }

    private static void ValidateTasks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        JsonArray tasks
    )
    {
        foreach (JsonNode? task in tasks)
        {
            if (
                Sql.Scalar(
                    connection,
                    "SELECT id FROM tasks WHERE id=$id;",
                    transaction,
                    ("$id", task!.GetValue<long>())
                )
                is null
            )
            {
                throw new OperationException("not_found", "A referenced task does not exist.");
            }
        }
    }

    private static JsonArray TaskIds(JsonObject arguments)
    {
        if (!arguments.TryGetPropertyValue("taskIds", out JsonNode? value))
        {
            return [];
        }

        if (value is not JsonArray array)
        {
            throw Invalid("taskIds must be an array of positive integer IDs.");
        }

        JsonArray normalized = [];
        foreach (JsonNode? item in array)
        {
            if (!TryInteger(item, out long id) || id <= 0)
            {
                throw Invalid("taskIds must be an array of positive integer IDs.");
            }

            normalized.Add(id);
        }

        return normalized;
    }

    private static void Fields(JsonObject arguments, string[] allowed)
    {
        if (arguments.Any(pair => !allowed.Contains(pair.Key, StringComparer.Ordinal)))
        {
            throw Invalid("Unknown request field.");
        }
    }

    private static string Text(JsonObject arguments, string key)
    {
        if (
            arguments[key] is not JsonValue value
            || !value.TryGetValue<string>(out string? text)
            || string.IsNullOrWhiteSpace(text)
        )
        {
            throw Invalid(key + " must be a nonempty string.");
        }

        return text;
    }

    private static string? OptionalText(JsonObject arguments, string key) =>
        arguments.ContainsKey(key) ? Text(arguments, key) : null;

    private static long Id(JsonObject arguments, string key)
    {
        if (!TryInteger(arguments[key], out long id) || id <= 0)
        {
            throw Invalid(key + " must be a positive JSON integer.");
        }

        return id;
    }

    private static (long Cursor, int Limit) Page(JsonObject arguments)
    {
        long cursor = 0;
        if (
            arguments.ContainsKey("cursor")
            && (!TryInteger(arguments["cursor"], out cursor) || cursor < 0)
        )
        {
            throw Invalid("cursor must be a nonnegative JSON integer.");
        }

        long limit = arguments.ContainsKey("limit") ? Id(arguments, "limit") : 50;
        if (limit > 200)
        {
            throw Invalid("limit must be between 1 and 200.");
        }

        return (cursor, (int)limit);
    }

    private static bool TryInteger(JsonNode? value, out long number)
    {
        number = 0;
        return value is JsonValue json
            && json.GetValueKind() == JsonValueKind.Number
            && JsonSerializer.SerializeToElement(json, Json.Options).TryGetInt64(out number);
    }

    private static bool SamePath(string first, string second) =>
        Path.GetFullPath(first)
            .TrimEnd(Path.DirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase
            );

    private static bool MatchesPath(string path, string pattern)
    {
        StringBuilder expression = new("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                expression.Append(".*");
                i++;
            }
            else
            {
                expression.Append(
                    pattern[i] switch
                    {
                        '*' => "[^/]*",
                        '?' => "[^/]",
                        _ => Regex.Escape(pattern[i].ToString()),
                    }
                );
            }
        }

        expression.Append('$');
        return Regex.IsMatch(
            path,
            expression.ToString(),
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );
    }

    private static OperationException Invalid(string message) => new("invalid_arguments", message);
}
