using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using static LiteAgensh.ToolArguments;

namespace LiteAgensh;

public sealed class GitOperations : IGitWorkspace
{
    private sealed record ChangeRecord(
        long Id,
        string Author,
        string Branch,
        string Head,
        string Title,
        string Body,
        long[] TaskIds,
        string State,
        string CreatedAt,
        string UpdatedAt
    );

    private readonly RunDatabase database;
    private readonly CollaborationOperations collaboration;
    private readonly GitWorktrees worktrees;
    private readonly GitCheckRunner checks;
    private readonly IntegrationHealthGate health;
    private string Stamp => database.Now.ToString("O", CultureInfo.InvariantCulture);

    public GitOperations(RunDatabase database, CollaborationOperations collaboration)
        : this(database, collaboration, null) { }

    internal GitOperations(
        RunDatabase database,
        CollaborationOperations collaboration,
        ConfiguredCheckInvocation? configuredCheck
    )
    {
        this.database = database;
        this.collaboration = collaboration;
        ProcessRunner processes = new(database);
        worktrees = new(database, collaboration, processes);
        checks = new(
            database, worktrees, processes, configuredCheck, RequireSettledProcessOwnership
        );
        health = new(
            database,
            collaboration,
            worktrees,
            checks,
            RequireSettledProcessOwnership,
            FindIntegratedCommitAuthor
        );
        EnsureSchema(database);
    }

    internal static void EnsureSchema(RunDatabase database)
    {
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
            """,
            transaction
        );
        GitCheckRunner.CreateSchema(connection, transaction);
        GitWorktrees.CreateSchema(connection, transaction);
        IntegrationHealthGate.CreateSchema(connection, transaction);
        transaction.Commit();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        RequireSettledProcessOwnership();
        await worktrees.InitializeRepositoryAsync(cancellationToken).ConfigureAwait(false);
    }

    public string GetWorkerPath(string worker) => worktrees.GetWorkerPath(worker);

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

            using var diagnosticActor = SqliteDiagnostics.UseActor(actor);
            return name switch
            {
                "change_write" => await WriteChangeAsync(actor, arguments, cancellationToken)
                    .ConfigureAwait(false),
                "change_read" => await ReadChangeAsync(arguments, cancellationToken)
                    .ConfigureAwait(false),
                "change_list" => ListChanges(arguments),
                "change_review" => Review(actor, arguments),
                "check_read" => checks.ReadChecks(
                    arguments,
                    static (connection, id) => _ = ReadChangeRecord(connection, null, id)
                ),
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
        cancellationToken.ThrowIfCancellationRequested();
        RequireSettledProcessOwnership();
        if (
            TryGetIdleExpectedHead(out string? expectedHead)
            && health.TryRecordIdlePass(expectedHead!)
        )
        {
            return;
        }

        await RecoverRequestsAsync(cancellationToken).ConfigureAwait(false);
        await health.EvaluateAsync(forceChecks: false, cancellationToken).ConfigureAwait(false);
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
                await RecoverOrFailRequestAsync(
                        id,
                        "unavailable",
                        "Integration cancelled; all worker work is retained."
                    )
                    .ConfigureAwait(false);
                throw;
            }
            catch (OperationException exception)
            {
                await RecoverOrFailRequestAsync(id, exception.Code, exception.Message)
                    .ConfigureAwait(false);
                RequireSettledProcessOwnership();
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or InvalidOperationException
                            or System.ComponentModel.Win32Exception
                )
            {
                await RecoverOrFailRequestAsync(id, "unavailable", exception.Message)
                    .ConfigureAwait(false);
                RequireSettledProcessOwnership();
            }
        }
    }

    private bool TryGetIdleExpectedHead(out string? expectedHead)
    {
        using SqliteConnection connection = database.Open();
        if (
            Convert.ToInt64(
                Sql.Scalar(
                    connection,
                    "SELECT EXISTS(SELECT 1 FROM integration_requests WHERE state IN "
                        + "('pending','running'));"
                ),
                CultureInfo.InvariantCulture
            ) != 0
        )
        {
            expectedHead = null;
            return false;
        }

        expectedHead =
            Sql.Scalar(
                connection,
                """
                SELECT COALESCE(
                    (SELECT integrated_commit FROM integration_requests
                        WHERE state='succeeded' ORDER BY id DESC LIMIT 1),
                    (SELECT value FROM meta WHERE key=$baseKey)
                );
                """,
                null,
                ("$baseKey", RunMetadata.GitBaseCommitKey)
            ) as string;
        return expectedHead is not null;
    }

    private async Task RecoverOrFailRequestAsync(long id, string code, string message)
    {
        if (!await TryRecoverAdvanceAsync(id).ConfigureAwait(false))
        {
            FinishRequest(id, false, code, message, null);
        }
    }

    private string? FindIntegratedCommitAuthor(string commit)
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

    public async Task<JsonObject> EvaluateHealthAsync(CancellationToken cancellationToken = default)
    {
        using FileStream ownership = AcquireOwnership();
        RequireInitialized();
        try
        {
            return Operation.Success(
                await health.EvaluateAsync(forceChecks: true, cancellationToken)
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

            await worktrees.RequireReadyIntegrationWorktreeAsync(cancellationToken)
                .ConfigureAwait(false);
            string head = await worktrees
                .ResolveAsync(worktrees.IntegrationBranch, cancellationToken)
                .ConfigureAwait(false);
            string batch = Guid.NewGuid().ToString("N");
            bool passed;
            string failureCode = "check_failed";
            string failureMessage =
                "Final checks failed; local delivery and unresolved work are retained.";
            try
            {
                passed = await checks.RunChecksAsync(
                        null,
                        "final",
                        head,
                        worktrees.IntegrationPath,
                        database.Configuration.FinalChecks,
                        batch,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                await worktrees
                    .RequireExactTreeAsync(worktrees.IntegrationPath, head, cancellationToken)
                    .ConfigureAwait(false);
                if (
                    await worktrees
                        .ResolveAsync(worktrees.IntegrationBranch, cancellationToken)
                        .ConfigureAwait(false) != head
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

            await worktrees.RetainDeliveryAsync(head, cancellationToken).ConfigureAwait(false);
            JsonObject report = BuildDeliveryReport(head, batch, passed);
            using SqliteConnection store = database.Open();
            using SqliteTransactionScope transaction = Sql.BeginTransaction(store);
            Sql.SetMetadata(
                store,
                transaction,
                RunMetadata.GitDeliveryReportKey,
                report.ToJsonString(Json.Options)
            );
            Sql.SetMetadata(store, transaction, RunMetadata.GitFinalizedKey, "true");
            worktrees.PublishEvent(
                store,
                transaction,
                "delivery",
                $"Local delivery {worktrees.DeliveryBranch} at {head}: "
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
        try
        {
            RequireQuiescent(allowFinalizing: false);
            if (!database.Meta.IsGitFinalized)
            {
                throw new OperationException(
                    "conflict",
                    "Inspect and finalize retained delivery before explicit cleanup."
                );
            }

            JsonArray removed = await worktrees.CleanupOwnedResourcesAsync(
                    RequireSettledRequestsForCleanup, cancellationToken
                )
                .ConfigureAwait(false);
            return Operation.Success(
                new
                {
                    removed,
                    retainedDeliveryRef = worktrees.DeliveryBranch,
                    statePath = database.StatePath,
                }
            );
        }
        catch (OperationException exception)
        {
            return Operation.Failure(exception.Code, exception.Message);
        }
    }

    private static void RequireSettledRequestsForCleanup(
        SqliteConnection connection,
        SqliteTransaction transaction
    )
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
                    Fields(arguments, "action", "title", "body", "taskIds");
                    string title = Text(arguments, "title");
                    string body = Text(arguments, "body");
                    long[] tasks = PositiveIntegers(arguments, "taskIds");
                    string branch = worktrees.WorkerBranch(actor);
                    string head = await worktrees.ResolveAsync(branch, token).ConfigureAwait(false);
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
                        ("$tasks", JsonSerializer.Serialize(tasks, Json.Options)),
                        ("$time", Stamp)
                    );
                    long id = LastId(connection, transaction);
                    worktrees.PublishEvent(
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
                    Fields(arguments, "action", "id", "title", "body", "taskIds", "head");
                    long id = Integer(arguments, "id");
                    if (
                        !arguments.Any(pair => pair.Key is "title" or "body" or "taskIds" or "head")
                    )
                    {
                        throw Invalid("An update requires at least one updated field.");
                    }

                    string? title = OptionalText(arguments, "title");
                    string? body = OptionalText(arguments, "body");
                    string? head = OptionalText(arguments, "head");
                    long[]? tasks = arguments.ContainsKey("taskIds")
                        ? PositiveIntegers(arguments, "taskIds")
                        : null;
                    if (
                        head is not null
                        && head
                            != await worktrees
                                .ResolveAsync(worktrees.WorkerBranch(actor), token)
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
                    ChangeRecord change = ReadChangeRecord(connection, transaction, id);
                    RequireAuthor(change, actor);
                    RequireOpen(change);
                    RequireNotRunning(connection, transaction, id);
                    if (tasks is not null)
                    {
                        ValidateTasks(connection, transaction, tasks);
                    }

                    if (head is not null && head != change.Head)
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
                        ("$title", title ?? change.Title),
                        ("$body", body ?? change.Body),
                        ("$tasks", JsonSerializer.Serialize(tasks ?? change.TaskIds, Json.Options)),
                        ("$head", head ?? change.Head),
                        ("$time", Stamp),
                        ("$id", id)
                    );
                    worktrees.PublishEvent(
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
                    Fields(arguments, "action", "id", "body");
                    long id = Integer(arguments, "id");
                    string body = Text(arguments, "body");
                    using SqliteConnection connection = database.Open();
                    using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                    RequireWritable(connection, transaction);
                    _ = ReadChangeRecord(connection, transaction, id);
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
                    worktrees.PublishEvent(
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
                    Fields(arguments, "action", "id", "head");
                    long id = Integer(arguments, "id");
                    string head = Text(arguments, "head");
                    using SqliteConnection connection = database.Open();
                    using SqliteTransactionScope transaction = Sql.BeginTransaction(connection);
                    RequireWritable(connection, transaction);
                    ChangeRecord change = ReadChangeRecord(connection, transaction, id);
                    RequireOpen(change);
                    if (head != change.Head)
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
                        worktrees.PublishEvent(
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
        Fields(arguments, "id");
        long id = Integer(arguments, "id");
        JsonObject change = Change(id);
        string head = change["head"]!.GetValue<string>();
        change["diff"] = await worktrees.ReadChangeDiffAsync(head, token).ConfigureAwait(false);
        using SqliteConnection connection = database.Open();
        change["integrationRequests"] = Requests(connection, id);
        return Operation.Success(change);
    }

    private JsonObject ListChanges(JsonObject arguments)
    {
        Fields(arguments, "state", "cursor", "limit");
        string? state = OptionalText(arguments, "state");
        if (state is not null and not ("open" or "integrated"))
        {
            throw Invalid("Unknown change state.");
        }

        (long cursor, int limit) = Pagination(arguments);
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
        Fields(arguments, "id", "head", "decision", "body");
        long changeId = Integer(arguments, "id");
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
        ChangeRecord change = ReadChangeRecord(connection, transaction, changeId);
        RequireOpen(change);
        if (change.Head != head)
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

        string createdAt = Stamp;
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
            ("$time", createdAt)
        );
        long id = LastId(connection, transaction);
        worktrees.PublishEvent(
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
            worktrees.PublishEvent(
                connection,
                transaction,
                "integration-started",
                $"Integration request {requestId} started."
            );
            transaction.Commit();
        }

        ChangeRecord change = ReadChangeRecord(changeId);
        RequireOpen(change);
        await RequireSubmittedHeadAsync(change, submitted, token).ConfigureAwait(false);
        health.Enforce(change.TaskIds);
        await worktrees.RequireReadyIntegrationWorktreeAsync(token).ConfigureAwait(false);
        string parent = await worktrees.ResolveAsync(worktrees.IntegrationBranch, token)
            .ConfigureAwait(false);
        await RequireReviewsAsync(change, parent, token).ConfigureAwait(false);
        GitWorktrees.CandidateWorktree candidate = await worktrees
            .CreateCandidateAsync(requestId, changeId, parent, submitted, token)
            .ConfigureAwait(false);

        await RequireReviewsAsync(change, parent, token, candidate.Commit).ConfigureAwait(false);

        using (SqliteConnection connection = database.Open())
        {
            Sql.Execute(
                connection,
                "UPDATE integration_requests SET "
                    + "parent_commit=$parent,candidate_commit=$candidate WHERE id=$id;",
                null,
                ("$parent", parent),
                ("$candidate", candidate.Commit),
                ("$id", requestId)
            );
        }

        bool passed = await checks.RunChecksAsync(
                changeId,
                "candidate",
                candidate.Commit,
                candidate.Path,
                database.Configuration.CandidateChecks,
                "candidate-" + requestId.ToString(CultureInfo.InvariantCulture),
                token
            )
            .ConfigureAwait(false);
        if (!passed)
        {
            throw new OperationException(
                "check_failed",
                $"Candidate checks failed; work and logs are retained at {candidate.Path}."
            );
        }

        await worktrees.RequireExactTreeAsync(candidate.Path, candidate.Commit, token)
            .ConfigureAwait(false);
        ChangeRecord current = ReadChangeRecord(changeId);
        await RequireSubmittedHeadAsync(current, submitted, token).ConfigureAwait(false);
        await RequireReviewsAsync(current, parent, token, candidate.Commit).ConfigureAwait(false);
        health.Enforce(current.TaskIds);
        if (
            await worktrees.ResolveAsync(worktrees.IntegrationBranch, token)
                .ConfigureAwait(false) != parent
            || await worktrees.ResolveAsync(candidate.Branch, token)
                .ConfigureAwait(false) != candidate.Commit
        )
        {
            throw new OperationException(
                "stale_candidate",
                "Integration or candidate moved during checks."
            );
        }

        await worktrees.RequireExactTreeAsync(worktrees.IntegrationPath, parent, token)
            .ConfigureAwait(false);

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

        await worktrees.AdvanceIntegrationAsync(candidate, current.Branch, submitted, parent, token)
            .ConfigureAwait(false);
        FinishRequest(
            requestId,
            true,
            "ok",
            "Exact checked merged candidate integrated.",
            candidate.Commit
        );
        await health.EvaluateAsync(forceChecks: true, token).ConfigureAwait(false);
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
            || await worktrees
                .ResolveAsync(worktrees.IntegrationBranch, CancellationToken.None)
                .ConfigureAwait(false) != candidate
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
            string current = await worktrees.ResolveAsync(worktrees.IntegrationBranch, token)
                .ConfigureAwait(false);
            if (ready && candidate is not null && current == candidate)
            {
                await worktrees.RecoverIntegrationWorktreeAsync(parent, candidate, token)
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
                Sql.SetMetadata(connection, transaction, RunMetadata.GitPlanIntegratedKey, "true");
            }
        }

        worktrees.PublishEvent(
            connection,
            transaction,
            "integration-result",
            $"Integration {id} {(succeeded ? "succeeded" : "failed")} ({code}): {message}"
        );
        transaction.Commit();
    }

    private async Task RequireReviewsAsync(
        ChangeRecord change,
        string parent,
        CancellationToken token,
        string? mergedCandidate = null
    )
    {
        string head = change.Head;
        Dictionary<string, string> latest = new(StringComparer.Ordinal);
        using (SqliteConnection connection = database.Open())
        using (
            SqliteCommand command = Sql.Command(
                connection,
                """
                SELECT actor,decision FROM reviews
                WHERE change_id=$id AND head=$head AND stale=0
                    AND actor<>$author AND decision<>'comment' ORDER BY id;
                """,
                null,
                ("$id", change.Id),
                ("$head", head),
                ("$author", change.Author)
            )
        )
        using (ObservedSqliteReader reader = Sql.Reader(command))
        {
            while (reader.Read())
            {
                latest[reader.GetString(0)] = reader.GetString(1);
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

        bool protectedChange = false;
        if (database.Configuration.ProtectedPaths.Length != 0)
        {
            string[] files = await worktrees.ReadChangedPathsAsync(
                    parent, head, mergedCandidate, token
                )
                .ConfigureAwait(false);
            protectedChange = files.Any(file =>
                database.Configuration.ProtectedPaths.Any(pattern => MatchesPath(file, pattern))
            );
        }

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
        ChangeRecord change,
        string head,
        CancellationToken token
    )
    {
        if (
            change.Head != head
            || await worktrees.ResolveAsync(change.Branch, token).ConfigureAwait(false) != head
        )
        {
            throw new OperationException(
                "stale_candidate",
                "The submitted worker head changed; update and review the new head."
            );
        }
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

        return new JsonObject
        {
            ["baseCommit"] = database.Configuration.BaseRevision,
            ["finalCommit"] = head,
            ["deliveryRef"] = worktrees.DeliveryBranch,
            ["accepted"] = passed,
            ["checks"] = GitCheckRunner.ListAll(connection),
            ["finalCheckBatch"] = batch,
            ["integrationHealth"] = health.Read(),
            ["unresolvedTasks"] = tasks,
            ["unresolvedChanges"] = unresolvedChanges,
            ["statePath"] = database.StatePath,
            ["createdAt"] = Stamp,
        };
    }

    private ChangeRecord ReadChangeRecord(long id)
    {
        using SqliteConnection connection = database.Open();
        return ReadChangeRecord(connection, null, id);
    }

    private static ChangeRecord ReadChangeRecord(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        long id
    )
    {
        using SqliteCommand command = Sql.Command(
            connection,
            """
            SELECT id,author,branch,head,title,body,task_ids,state,created_at,updated_at
            FROM changes WHERE id=$id;
            """,
            transaction,
            ("$id", id)
        );
        using ObservedSqliteReader reader = Sql.Reader(command);
        if (!reader.Read())
        {
            throw new OperationException("not_found", "Change does not exist.");
        }

        return new ChangeRecord(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            JsonSerializer.Deserialize<long[]>(reader.GetString(6), Json.Options)!,
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9)
        );
    }

    private JsonObject Change(long id)
    {
        using SqliteConnection connection = database.Open();
        ChangeRecord record = ReadChangeRecord(connection, null, id);
        JsonObject change = new()
        {
            ["id"] = record.Id,
            ["author"] = record.Author,
            ["branch"] = record.Branch,
            ["head"] = record.Head,
            ["title"] = record.Title,
            ["body"] = record.Body,
            ["taskIds"] = new JsonArray(record.TaskIds.Select(task => (JsonNode)task).ToArray()),
            ["state"] = record.State,
            ["createdAt"] = record.CreatedAt,
            ["updatedAt"] = record.UpdatedAt,
        };

        JsonArray comments = [];
        using (
            SqliteCommand command = Sql.Command(
                connection,
                "SELECT id,actor,body,created_at FROM change_comments WHERE "
                    + "change_id=$id ORDER BY id;",
                null,
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
                null,
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
        change["checks"] = GitCheckRunner.ListForChange(connection, null, id);
        return change;
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
        if (!database.Meta.IsGitActive())
        {
            throw new OperationException(
                "unavailable",
                "Git worktrees are not initialized or have been cleaned up."
            );
        }
    }

    private void RequireQuiescent(bool allowFinalizing)
    {
        string? lifecycle = database.Meta.RawLifecycle;
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
        if (!RunMetadata.IsGitWritable(connection, transaction))
        {
            throw new OperationException(
                "conflict",
                "Explicit cleanup has frozen Git domain mutations."
            );
        }
    }

    private static long LastId(SqliteConnection connection, SqliteTransaction transaction) =>
        Convert.ToInt64(
            Sql.Scalar(connection, "SELECT last_insert_rowid();", transaction),
            CultureInfo.InvariantCulture
        );

    private static void RequireAuthor(ChangeRecord change, string actor)
    {
        if (change.Author != actor)
        {
            throw new OperationException("not_visible", "Only the change author may update it.");
        }
    }

    private static void RequireOpen(ChangeRecord change)
    {
        if (change.State != "open")
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
        long[] tasks
    )
    {
        foreach (long task in tasks)
        {
            if (
                Sql.Scalar(
                    connection,
                    "SELECT id FROM tasks WHERE id=$id;",
                    transaction,
                    ("$id", task)
                )
                is null
            )
            {
                throw new OperationException("not_found", "A referenced task does not exist.");
            }
        }
    }

    internal static bool MatchesPath(string path, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        string normalizedPath = path.TrimStart('/');
        string normalizedPattern = pattern.Replace('\\', '/').TrimStart('/');
        if (normalizedPattern == "**")
        {
            return true;
        }

        StringBuilder expression = new("^");
        for (int i = 0; i < normalizedPattern.Length; i++)
        {
            if (
                normalizedPattern[i] == '*'
                && i + 1 < normalizedPattern.Length
                && normalizedPattern[i + 1] == '*'
            )
            {
                if (i + 2 < normalizedPattern.Length && normalizedPattern[i + 2] == '/')
                {
                    expression.Append("(?:.*/)?");
                    i += 2;
                }
                else
                {
                    expression.Append(".*");
                    i++;
                }
            }
            else
            {
                expression.Append(
                    normalizedPattern[i] switch
                    {
                        '*' => "[^/]*",
                        '?' => "[^/]",
                        _ => Regex.Escape(normalizedPattern[i].ToString()),
                    }
                );
            }
        }

        expression.Append('$');
        return Regex.IsMatch(
            normalizedPath,
            expression.ToString(),
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );
    }
}
