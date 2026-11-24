using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class ConfigurationSnapshotSpecs(TestContext context)
{
    private const string Goal =
        "# Repair parsing\n\nKeep quoted values intact.\nReport checked results.\n";
    private const string MinimalYaml = """
        repositoryPath: ./checkout
        statePath: ./run
        campaignLedgerPath: ./campaign/credits.sqlite
        goalFile: ./goal.md
        """;

    [TestMethod]
    [DataRow(".yaml")]
    [DataRow(".YML")]
    public async Task LoadAsyncConfigDirectoryOwnsRelativePathsNotLaunchDirectory(string extension)
    {
        using ScratchDirectory scratch = new();
        string directory = scratch.PathFor("input");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "goal.md"), Goal);
        string input = Path.Combine(directory, "run" + extension);
        File.WriteAllText(
            input,
            MinimalYaml
                + """

                baseRevision: feature/parser
                nativeExecutable: ./tools/copilot
                nativeArguments: ['--effort=high']
                protectedPaths: ['src/**']
                """
        );
        string launchDirectory = Environment.CurrentDirectory;

        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            input,
            context.CancellationToken
        );
        loaded.Validate();

        Assert.AreEqual(Path.Combine(directory, "checkout"), loaded.RepositoryPath);
        Assert.AreEqual(Path.Combine(directory, "run"), loaded.StatePath);
        Assert.AreEqual(
            Path.Combine(directory, "campaign", "credits.sqlite"),
            loaded.CampaignLedgerPath
        );
        Assert.AreEqual(Goal, loaded.Goal);
        Assert.AreEqual("./tools/copilot", loaded.NativeExecutable);
        Assert.AreEqual("feature/parser", loaded.BaseRevision);
        Assert.AreEqual("high", loaded.GetReasoningEffort());
        Assert.Contains("src/**", loaded.ProtectedPaths);
        Assert.AreEqual(launchDirectory, Environment.CurrentDirectory);
        Assert.IsFalse(Directory.Exists(loaded.StatePath));
        Assert.IsFalse(File.Exists(loaded.CampaignLedgerPath));
    }

    [TestMethod]
    public async Task LoadAsyncSameRelativeNamesInTwoDirectoriesDoNotShareGoalOrState()
    {
        using ScratchDirectory scratch = new();
        string first = scratch.PathFor("first");
        string second = scratch.PathFor("second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "run.yaml"), MinimalYaml);
        File.WriteAllText(Path.Combine(second, "run.yaml"), MinimalYaml);
        File.WriteAllText(Path.Combine(first, "goal.md"), Goal);
        File.WriteAllText(
            Path.Combine(second, "goal.md"),
            "# Review delivery\n\nDo not change parsing.\n"
        );

        RunConfiguration a = await ConfigurationInput.LoadAsync(
            Path.Combine(first, "run.yaml"),
            context.CancellationToken
        );
        RunConfiguration b = await ConfigurationInput.LoadAsync(
            Path.Combine(second, "run.yaml"),
            context.CancellationToken
        );

        Assert.AreEqual(Goal, a.Goal);
        Assert.AreEqual("# Review delivery\n\nDo not change parsing.\n", b.Goal);
        Assert.AreEqual(Path.Combine(first, "run"), a.StatePath);
        Assert.AreEqual(Path.Combine(second, "run"), b.StatePath);
        Assert.IsFalse(Directory.Exists(a.StatePath));
        Assert.IsFalse(Directory.Exists(b.StatePath));
    }

    [TestMethod]
    [DataRow(null, "per-session")]
    [DataRow("per-session", "per-session")]
    [DataRow("shared", "shared")]
    public async Task LoadAsyncLegacyJsonPreservesAbsolutePathsAndAllocation(
        string? allocation,
        string expectedAllocation
    )
    {
        using ScratchDirectory scratch = new();
        JsonObject input = new()
        {
            ["repositoryPath"] = scratch.PathFor("checkout"),
            ["statePath"] = scratch.PathFor("run"),
            ["campaignLedgerPath"] = scratch.PathFor("credits.sqlite"),
            ["goal"] = Goal,
            ["workerStartIntervalSeconds"] = 0,
        };
        if (allocation is not null)
        {
            input["creditAllocation"] = allocation;
        }

        string path = scratch.PathFor("legacy.json");
        File.WriteAllText(path, input.ToJsonString());

        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            path,
            context.CancellationToken
        );
        loaded.Validate();

        Assert.AreEqual(scratch.PathFor("checkout"), loaded.RepositoryPath);
        Assert.AreEqual(scratch.PathFor("run"), loaded.StatePath);
        Assert.AreEqual(scratch.PathFor("credits.sqlite"), loaded.CampaignLedgerPath);
        Assert.AreEqual(Goal, loaded.Goal);
        Assert.AreEqual(expectedAllocation, loaded.CreditAllocation);
        Assert.AreEqual(0, loaded.WorkerStartIntervalSeconds);
        Assert.AreEqual(10000m, loaded.RunAiCreditAllowance);
        Assert.IsFalse(Directory.Exists(loaded.StatePath));
    }

    [TestMethod]
    [DataRow("repositoryPath", "checkout")]
    [DataRow("statePath", "run")]
    public async Task LoadAsyncLegacyJsonRelativePathsAreNotReinterpretedAsYaml(
        string field,
        string relative
    )
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("legacy.json");
        JsonObject input = new()
        {
            ["repositoryPath"] = scratch.PathFor("checkout"),
            ["statePath"] = scratch.PathFor("run"),
            ["campaignLedgerPath"] = scratch.PathFor("credits.sqlite"),
            ["goal"] = Goal,
        };
        input[field] = relative;
        File.WriteAllText(path, input.ToJsonString());

        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            path,
            context.CancellationToken
        );
        (
            loaded with
            {
                RepositoryPath = scratch.PathFor("checkout"),
                StatePath = scratch.PathFor("run"),
            }
        ).Validate();
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(loaded.Validate);

        Assert.AreEqual(
            relative,
            field == "repositoryPath" ? loaded.RepositoryPath : loaded.StatePath
        );
        Assert.AreEqual(
            "Repository, state and campaign ledger paths must be absolute.",
            error.Message
        );
        Assert.IsFalse(Directory.Exists(scratch.PathFor("run")));
    }

    [TestMethod]
    public async Task RunDatabaseRelativeLegacyLedgerRejectsBeforeCreatingState()
    {
        using ScratchDirectory scratch = new();
        string ledger = $"owned-relative-ledger-{Guid.NewGuid():N}.sqlite";
        string path = scratch.PathFor("legacy.json");
        File.WriteAllText(
            path,
            new JsonObject
            {
                ["repositoryPath"] = scratch.PathFor("checkout"),
                ["statePath"] = scratch.PathFor("run"),
                ["campaignLedgerPath"] = ledger,
                ["goal"] = Goal,
            }.ToJsonString()
        );
        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            path,
            context.CancellationToken
        );
        (loaded with { CampaignLedgerPath = scratch.PathFor("credits.sqlite") }).Validate();

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new RunDatabase(loaded.StatePath, loaded)
        );

        Assert.AreEqual(ledger, loaded.CampaignLedgerPath);
        Assert.AreEqual(
            "Repository, state and campaign ledger paths must be absolute.",
            error.Message
        );
        Assert.IsFalse(Directory.Exists(loaded.StatePath));
        Assert.IsFalse(File.Exists(ledger));
    }

    [TestMethod]
    public async Task RunDatabaseSourceGoalEditAndRemovalDoNotChangePersistedSnapshot()
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("run.yaml");
        string goalPath = scratch.PathFor("goal.md");
        File.WriteAllText(path, MinimalYaml + "\ncreditAllocation: shared\n");
        File.WriteAllText(goalPath, Goal);
        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            path,
            context.CancellationToken
        );
        RunDatabase initialized = new(loaded.StatePath, loaded);
        string runId = initialized.RunId;

        File.WriteAllText(goalPath, "# Changed goal\n\nThis must not replace the original.\n");
        RunDatabase afterEdit = new(loaded.StatePath);
        // Removing the owned input proves reopen does not depend on the source file at all.
        File.Delete(goalPath);
        RunDatabase afterRemoval = new(loaded.StatePath);
        JsonObject stored = JsonNode.Parse(afterRemoval.ReadMetadata("configuration")!)!.AsObject();

        Assert.AreEqual(Goal, afterEdit.Configuration.Goal);
        Assert.AreEqual(Goal, afterRemoval.Configuration.Goal);
        Assert.AreEqual(Goal, stored["goal"]!.GetValue<string>());
        Assert.IsFalse(stored.ContainsKey("goalFile"));
        Assert.AreEqual("shared", afterRemoval.Configuration.CreditAllocation);
        Assert.AreEqual(runId, afterRemoval.RunId);
        Assert.AreEqual(loaded.RepositoryPath, afterRemoval.Configuration.RepositoryPath);
        Assert.IsFalse(File.Exists(loaded.CampaignLedgerPath));
    }

    [TestMethod]
    [DataRow("malformed", "Invalid YAML configuration:")]
    [DataRow("duplicate", "Invalid YAML configuration:")]
    [DataRow("multiple-documents", "Invalid YAML configuration:")]
    [DataRow("inline-goal", "YAML configuration requires goalFile, not inline goal.")]
    [DataRow("missing-goal-path", "YAML configuration requires a nonempty goalFile path.")]
    [DataRow("empty-goal-path", "YAML configuration requires a nonempty goalFile path.")]
    [DataRow("wrong-goal-path-type", "YAML configuration requires a nonempty goalFile path.")]
    [DataRow("null-array-item", "YAML string arrays cannot contain null items.")]
    public async Task LoadAsyncInvalidYamlStructureOrGoalAuthorityRejectsBeforeState(
        string scenario,
        string diagnostic
    )
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), Goal);
        string yaml = scenario switch
        {
            "malformed" => "repositoryPath: [",
            "duplicate" => MinimalYaml + "\nstatePath: ./other\n",
            "multiple-documents" => MinimalYaml + "\n---\nother: document\n",
            "inline-goal" => MinimalYaml + "\ngoal: conflicting inline goal\n",
            "missing-goal-path" => MinimalYaml.Replace("goalFile: ./goal.md", string.Empty),
            "empty-goal-path" => MinimalYaml.Replace("./goal.md", "'   '"),
            "wrong-goal-path-type" => MinimalYaml.Replace("./goal.md", "42"),
            "null-array-item" => MinimalYaml + "\nnativeArguments: [null]\n",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        string path = scratch.PathFor("invalid.yaml");
        File.WriteAllText(path, yaml);

        ArgumentException error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ConfigurationInput.LoadAsync(path, context.CancellationToken)
        );

        Assert.StartsWith(diagnostic, error.Message);
        Assert.IsFalse(Directory.Exists(scratch.PathFor("run")));
        Assert.IsFalse(File.Exists(scratch.PathFor("campaign", "credits.sqlite")));
    }

    [TestMethod]
    [DataRow("unknown", "unexpectedSetting")]
    [DataRow("missing-required", "repositoryPath")]
    [DataRow("wrong-integer-type", "$.workerCount")]
    [DataRow("wrong-array-type", "$.protectedPaths")]
    [DataRow("null-required", "$.repositoryPath")]
    public async Task LoadAsyncStrictYamlTypedFieldsRejectsBeforeState(
        string scenario,
        string diagnostic
    )
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), Goal);
        string yaml = scenario switch
        {
            "unknown" => MinimalYaml + "\nunexpectedSetting: true\n",
            "missing-required" => MinimalYaml.Replace("repositoryPath: ./checkout", string.Empty),
            "wrong-integer-type" => MinimalYaml + "\nworkerCount: many\n",
            "wrong-array-type" => MinimalYaml + "\nprotectedPaths: src/**\n",
            "null-required" => MinimalYaml.Replace("./checkout", "null"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        string path = scratch.PathFor("invalid.yml");
        File.WriteAllText(path, yaml);

        JsonException error = await Assert.ThrowsExactlyAsync<JsonException>(() =>
            ConfigurationInput.LoadAsync(path, context.CancellationToken)
        );

        Assert.Contains(diagnostic, error.Message);
        Assert.IsFalse(Directory.Exists(scratch.PathFor("run")));
        Assert.IsFalse(File.Exists(scratch.PathFor("campaign", "credits.sqlite")));
    }

    [TestMethod]
    [DataRow("{", "$")]
    [DataRow("null", null)]
    [DataRow("{\"goal\":\"x\",\"unexpected\":true}", "$.unexpected")]
    [DataRow("{\"goal\":\"x\"}", "$")]
    [DataRow("{\"repositoryPath\":42}", "$.repositoryPath")]
    public async Task LoadAsyncInvalidLegacyJsonDoesNotInitializeState(
        string json,
        string? expectedPath
    )
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("invalid.json");
        File.WriteAllText(path, json);

        if (expectedPath is null)
        {
            ArgumentException error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
                ConfigurationInput.LoadAsync(path, context.CancellationToken)
            );
            Assert.AreEqual("Configuration must be a JSON object.", error.Message);
        }
        else
        {
            JsonException error = await Assert.ThrowsExactlyAsync<JsonException>(() =>
                ConfigurationInput.LoadAsync(path, context.CancellationToken)
            );
            Assert.AreEqual(expectedPath, error.Path);
        }

        Assert.IsFalse(Directory.Exists(scratch.PathFor("run")));
        Assert.IsFalse(File.Exists(scratch.PathFor("credits.sqlite")));
    }

    [TestMethod]
    public async Task LoadAsyncNullLegacyProtectedPathRejectsBeforeState()
    {
        using ScratchDirectory scratch = new();
        JsonObject configuration = new()
        {
            ["repositoryPath"] = scratch.PathFor("checkout"),
            ["statePath"] = scratch.PathFor("run"),
            ["campaignLedgerPath"] = scratch.PathFor("credits.sqlite"),
            ["goal"] = Goal,
            ["protectedPaths"] = new JsonArray((JsonNode?)null),
        };
        string path = scratch.PathFor("invalid.json");
        File.WriteAllText(path, configuration.ToJsonString());

        ArgumentException error = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            ConfigurationInput.LoadAsync(path, context.CancellationToken)
        );

        Assert.AreEqual("JSON string arrays cannot contain null items.", error.Message);
        Assert.IsFalse(Directory.Exists(scratch.PathFor("run")));
        Assert.IsFalse(File.Exists(scratch.PathFor("credits.sqlite")));
    }

    [TestMethod]
    public async Task LoadAsyncMissingMarkdownReportsMissingFileBeforeState()
    {
        using ScratchDirectory scratch = new();
        string path = scratch.PathFor("run.yaml");
        File.WriteAllText(path, MinimalYaml);

        FileNotFoundException error = await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            ConfigurationInput.LoadAsync(path, context.CancellationToken)
        );

        Assert.AreEqual(scratch.PathFor("goal.md"), error.FileName);
        Assert.IsFalse(Directory.Exists(scratch.PathFor("run")));
        Assert.IsFalse(File.Exists(scratch.PathFor("campaign", "credits.sqlite")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" \r\n\t")]
    public async Task RunDatabaseEmptyMarkdownRejectsBeforeCreatingRunDirectory(string body)
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), body);
        File.WriteAllText(scratch.PathFor("run.yaml"), MinimalYaml);
        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            scratch.PathFor("run.yaml"),
            context.CancellationToken
        );

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new RunDatabase(loaded.StatePath, loaded)
        );

        Assert.AreEqual("Goal", error.ParamName);
        Assert.IsFalse(Directory.Exists(loaded.StatePath));
        Assert.IsFalse(File.Exists(loaded.CampaignLedgerPath));
    }

    [TestMethod]
    [DataRow("equal")]
    [DataRow("SHARED")]
    [DataRow("")]
    public async Task RunDatabaseInvalidAllocationRejectsBeforeCreatingRunDirectory(
        string allocation
    )
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), Goal);
        File.WriteAllText(
            scratch.PathFor("run.yaml"),
            MinimalYaml + $"\ncreditAllocation: '{allocation}'\n"
        );
        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            scratch.PathFor("run.yaml"),
            context.CancellationToken
        );

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new RunDatabase(loaded.StatePath, loaded)
        );

        Assert.AreEqual("Credit allocation must be per-session or shared.", error.Message);
        Assert.IsFalse(Directory.Exists(loaded.StatePath));
        Assert.IsFalse(File.Exists(loaded.CampaignLedgerPath));
    }

    [TestMethod]
    public void RunDatabaseMissingInitializationDoesNotCreateDirectory()
    {
        using ScratchDirectory scratch = new();
        string state = scratch.PathFor("not-initialized");

        FileNotFoundException error = Assert.ThrowsExactly<FileNotFoundException>(() =>
            new RunDatabase(state)
        );

        Assert.AreEqual(Path.Combine(state, "run.sqlite"), error.FileName);
        Assert.IsFalse(Directory.Exists(state));
    }

    [TestMethod]
    public void RunDatabaseIncompatibleSchemaPreservesMetadataAndSentinel()
    {
        using ScratchDirectory scratch = new();
        string state = scratch.PathFor("state");
        Directory.CreateDirectory(state);
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(state, "run.sqlite"),
            Pooling = false,
        }.ToString();
        using (SqliteConnection seed = new(connectionString))
        {
            seed.Open();
            using SqliteCommand command = seed.CreateCommand();
            command.CommandText = """
                CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                INSERT INTO meta VALUES('schema_version','99'),('sentinel','keep-original');
                """;
            command.ExecuteNonQuery();
        }

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RunDatabase(state)
        );

        Assert.AreEqual("Unsupported run schema 99; state has not been recreated.", error.Message);
        using SqliteConnection check = new(connectionString);
        check.Open();
        Assert.AreEqual(
            "99",
            Sql.Scalar(check, "SELECT value FROM meta WHERE key='schema_version';")
        );
        Assert.AreEqual(
            "keep-original",
            Sql.Scalar(check, "SELECT value FROM meta WHERE key='sentinel';")
        );
        Assert.AreEqual(2L, Sql.Scalar(check, "SELECT COUNT(*) FROM meta;"));
    }

    [TestMethod]
    public async Task RunDatabaseReinitializationCannotOverwriteExistingIdentityOrGoal()
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), Goal);
        File.WriteAllText(scratch.PathFor("run.yaml"), MinimalYaml);
        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            scratch.PathFor("run.yaml"),
            context.CancellationToken
        );
        RunDatabase original = new(loaded.StatePath, loaded);
        string identity = original.RunId;

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RunDatabase(loaded.StatePath, loaded with { Goal = "Replace the goal." })
        );
        RunDatabase reopened = new(loaded.StatePath);

        Assert.AreEqual("An initialized run cannot be overwritten by init.", error.Message);
        Assert.AreEqual(identity, reopened.RunId);
        Assert.AreEqual(Goal, reopened.Configuration.Goal);
    }

    [TestMethod]
    public async Task PublishedSchemaAndExampleAgreeWithGoalAndAllocationInputContract()
    {
        string project = FindProductDirectory();
        string schemaPath = Path.Combine(project, "schemas", "run.schema.json");
        string examplePath = Path.Combine(project, "examples", "run.yaml");
        JsonObject schema = JsonNode
            .Parse(File.ReadAllText(schemaPath))!
            .AsObject();
        JsonArray required = schema["required"]!.AsArray();
        JsonObject properties = schema["properties"]!.AsObject();

        Assert.HasCount(4, required);
        foreach (
            string field in new[]
            {
                "repositoryPath",
                "statePath",
                "campaignLedgerPath",
                "goalFile",
            }
        )
        {
            Assert.Contains(field, required.Select(node => node!.GetValue<string>()));
        }

        Assert.IsFalse(schema["additionalProperties"]!.GetValue<bool>());
        Assert.IsFalse(properties.ContainsKey("goal"));
        Assert.AreEqual("string", properties["goalFile"]!["type"]!.GetValue<string>());
        Assert.AreEqual(1, properties["goalFile"]!["minLength"]!.GetValue<int>());
        Assert.AreEqual("string", properties["creditAllocation"]!["type"]!.GetValue<string>());
        Assert.AreEqual(
            "per-session",
            properties["creditAllocation"]!["default"]!.GetValue<string>()
        );
        string[] allocations = properties["creditAllocation"]!["enum"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();
        Assert.HasCount(2, allocations);
        Assert.Contains("per-session", allocations);
        Assert.Contains("shared", allocations);
        JsonObject permission = Assert.IsInstanceOfType<JsonObject>(
            properties["nativePermissionPolicy"]
        );
        Assert.AreEqual("string", permission["type"]!.GetValue<string>());
        Assert.AreEqual("deny", permission["default"]!.GetValue<string>());
        string[] permissionChoices = permission["enum"]!.AsArray()
            .Select(node => node!.GetValue<string>()).ToArray();
        Assert.HasCount(2, permissionChoices);
        Assert.Contains("deny", permissionChoices);
        Assert.Contains("trusted", permissionChoices);
        Assert.AreEqual(0, properties["workerStartIntervalSeconds"]!["minimum"]!.GetValue<int>());
        Assert.AreEqual(30, properties["runAiCreditAllowance"]!["minimum"]!.GetValue<int>());
        Assert.AreEqual(
            "# yaml-language-server: $schema="
                + Path.GetRelativePath(Path.GetDirectoryName(examplePath)!, schemaPath)
                    .Replace('\\', '/'),
            File.ReadLines(examplePath).First()
        );

        RunConfiguration example = await ConfigurationInput.LoadAsync(
            examplePath,
            context.CancellationToken
        );
        example.Validate();

        Assert.StartsWith("# Review the LiteAgensh migration", example.Goal);
        Assert.Contains("Do not start paid native", example.Goal);
        Assert.AreEqual("shared", example.CreditAllocation);
        Assert.AreEqual("trusted", example.NativePermissionPolicy);
        Assert.Contains(example.NativePermissionPolicy, permissionChoices);
        Assert.AreEqual(10, example.WorkerCount);
        Assert.AreEqual(30, example.WorkerStartIntervalSeconds);
        Assert.AreEqual(Path.Combine(project, "examples", "run"), example.StatePath);
    }

    [TestMethod]
    public async Task LoadAsyncEquivalentYamlAndLegacyJsonMaterializeSameBusinessSnapshot()
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), Goal);
        File.WriteAllText(
            scratch.PathFor("run.yaml"),
            MinimalYaml + "\ncreditAllocation: shared\n"
        );
        JsonObject legacy = new()
        {
            ["repositoryPath"] = scratch.PathFor("checkout"),
            ["statePath"] = scratch.PathFor("run"),
            ["campaignLedgerPath"] = scratch.PathFor("campaign", "credits.sqlite"),
            ["goal"] = Goal,
            ["creditAllocation"] = "shared",
        };
        File.WriteAllText(scratch.PathFor("run.json"), legacy.ToJsonString());

        RunConfiguration yaml = await ConfigurationInput.LoadAsync(
            scratch.PathFor("run.yaml"),
            context.CancellationToken
        );
        RunConfiguration json = await ConfigurationInput.LoadAsync(
            scratch.PathFor("run.json"),
            context.CancellationToken
        );
        yaml.Validate();
        json.Validate();

        foreach (RunConfiguration loaded in new[] { yaml, json })
        {
            Assert.AreEqual(Goal, loaded.Goal);
            Assert.AreEqual(scratch.PathFor("checkout"), loaded.RepositoryPath);
            Assert.AreEqual(scratch.PathFor("run"), loaded.StatePath);
            Assert.AreEqual(
                scratch.PathFor("campaign", "credits.sqlite"),
                loaded.CampaignLedgerPath
            );
            Assert.AreEqual("shared", loaded.CreditAllocation);
            Assert.AreEqual(2, loaded.WorkerCount);
            Assert.AreEqual(0, loaded.WorkerStartIntervalSeconds);
            Assert.AreEqual("deny", loaded.NativePermissionPolicy);
            Assert.AreEqual(10000m, loaded.RunAiCreditAllowance);
            Assert.IsFalse(Directory.Exists(loaded.StatePath));
            Assert.IsFalse(File.Exists(loaded.CampaignLedgerPath));
        }
    }

    [TestMethod]
    [DataRow("workerCount: 0")]
    [DataRow("workerStartIntervalSeconds: -1")]
    [DataRow("durationSeconds: 0")]
    [DataRow("planningSeconds: -1")]
    [DataRow("planningSeconds: 1801")]
    [DataRow("runAiCreditAllowance: 29")]
    [DataRow("campaignAiCreditAllowance: 9999")]
    public async Task RunDatabaseInvalidAllocationBoundsRejectsBeforeCreatingState(string setting)
    {
        using ScratchDirectory scratch = new();
        File.WriteAllText(scratch.PathFor("goal.md"), Goal);
        File.WriteAllText(scratch.PathFor("run.yaml"), MinimalYaml + "\n" + setting + "\n");
        RunConfiguration loaded = await ConfigurationInput.LoadAsync(
            scratch.PathFor("run.yaml"),
            context.CancellationToken
        );

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new RunDatabase(loaded.StatePath, loaded)
        );

        Assert.AreEqual(
            "Worker count, deadlines and native credit allowances must be valid.",
            error.Message
        );
        Assert.IsFalse(Directory.Exists(loaded.StatePath));
        Assert.IsFalse(File.Exists(loaded.CampaignLedgerPath));
    }

    private static string FindProductDirectory()
    {
        for (
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "private",
                "app",
                "lite-agensh"
            );
            if (File.Exists(Path.Combine(candidate, "schemas", "run.schema.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "Cannot locate the published LiteAgensh schema and example."
        );
    }
}
