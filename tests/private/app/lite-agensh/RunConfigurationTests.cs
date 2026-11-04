using System.Text.Json;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class RunConfigurationTests
{
    [TestMethod]
    [DataRow("relative-repository")]
    [DataRow("relative-state")]
    [DataRow("relative-ledger")]
    [DataRow("state-is-checkout")]
    [DataRow("state-is-checkout-different-case")]
    [DataRow("state-below-checkout")]
    [DataRow("ledger-is-checkout")]
    [DataRow("ledger-below-checkout")]
    [DataRow("empty-goal")]
    [DataRow("zero-workers")]
    [DataRow("zero-duration")]
    [DataRow("planning-after-deadline")]
    [DataRow("insufficient-session-allowance")]
    [DataRow("campaign-smaller-than-run")]
    [DataRow("unknown-permission-policy")]
    public void InitializeInvalidConfigurationRejectsBeforeCreatingState(string scenario)
    {
        using RunFixture fixture = new();
        RunConfiguration configuration = scenario switch
        {
            "relative-repository" => fixture.Configuration with { RepositoryPath = "repository" },
            "relative-state" => fixture.Configuration with { StatePath = "state" },
            "relative-ledger" => fixture.Configuration with
            {
                CampaignLedgerPath = "credits.sqlite",
            },
            "state-is-checkout" => fixture.Configuration with
            {
                StatePath = fixture.RepositoryPath,
            },
            "state-is-checkout-different-case" => fixture.Configuration with
            {
                StatePath = fixture.RepositoryPath.ToUpperInvariant(),
            },
            "state-below-checkout" => fixture.Configuration with
            {
                StatePath = Path.Combine(fixture.RepositoryPath, "nested", "state"),
            },
            "ledger-is-checkout" => fixture.Configuration with
            {
                CampaignLedgerPath = fixture.RepositoryPath,
            },
            "ledger-below-checkout" => fixture.Configuration with
            {
                CampaignLedgerPath = Path.Combine(fixture.RepositoryPath, "credits.sqlite"),
            },
            "empty-goal" => fixture.Configuration with { Goal = " " },
            "zero-workers" => fixture.Configuration with { WorkerCount = 0 },
            "zero-duration" => fixture.Configuration with { DurationSeconds = 0 },
            "planning-after-deadline" => fixture.Configuration with
            {
                DurationSeconds = 60,
                PlanningSeconds = 61,
            },
            "insufficient-session-allowance" => fixture.Configuration with
            {
                RunAiCreditAllowance = 29,
            },
            "campaign-smaller-than-run" => fixture.Configuration with
            {
                RunAiCreditAllowance = 60,
                CampaignAiCreditAllowance = 59,
            },
            "unknown-permission-policy" => fixture.Configuration with
            {
                NativePermissionPolicy = "approve-everything",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        string instructions = Path.Combine(fixture.RepositoryPath, "AGENTS.md");
        File.WriteAllText(instructions, "Keep these owner-authored instructions.");

        Assert.ThrowsExactly<ArgumentException>(() =>
            new RunDatabase(configuration.StatePath, configuration)
        );

        Assert.IsFalse(Directory.Exists(fixture.StatePath));
        Assert.IsFalse(File.Exists(Path.Combine(configuration.StatePath, "run.sqlite")));
        Assert.IsFalse(File.Exists(configuration.CampaignLedgerPath));
        Assert.AreEqual("Keep these owner-authored instructions.", File.ReadAllText(instructions));
    }

    [TestMethod]
    [DataRow("candidate")]
    [DataRow("integration")]
    [DataRow("final")]
    public void InitializeCheckWithoutFiniteDeadlineRejectsBeforeCreatingState(string phase)
    {
        using RunFixture fixture = new();
        CheckCommand[] checks = [new() { Executable = "pwsh", TimeoutSeconds = 0 }];
        RunConfiguration configuration = phase switch
        {
            "candidate" => fixture.Configuration with { CandidateChecks = checks },
            "integration" => fixture.Configuration with { IntegrationChecks = checks },
            "final" => fixture.Configuration with { FinalChecks = checks },
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new RunDatabase(fixture.StatePath, configuration)
        );

        Assert.Contains("timeout", error.Message);
        Assert.IsFalse(Directory.Exists(fixture.StatePath));
    }

    [TestMethod]
    [DataRow(1, 0, "deny")]
    [DataRow(2, 60, "trusted")]
    public void InitializeValidBoundaryConfigurationCreatesRecoverableRun(
        int workers,
        int planningSeconds,
        string policy
    )
    {
        using RunFixture fixture = new();
        RunConfiguration configuration = fixture.Configuration with
        {
            WorkerCount = workers,
            DurationSeconds = 60,
            PlanningSeconds = planningSeconds,
            RunAiCreditAllowance = 30,
            CampaignAiCreditAllowance = 30,
            NativePermissionPolicy = policy,
            CandidateChecks = [new() { Executable = "pwsh", TimeoutSeconds = 1 }],
        };

        RunDatabase database = new(fixture.StatePath, configuration);
        RunDatabase recovered = new(fixture.StatePath);

        Assert.HasCount(workers + 1, recovered.Members);
        Assert.Contains("owner", recovered.Members);
        Assert.Contains("worker1", recovered.Members);
        Assert.AreEqual(database.RunId, recovered.RunId);
        Assert.AreEqual(planningSeconds, recovered.Configuration.PlanningSeconds);
        Assert.AreEqual(policy, recovered.Configuration.NativePermissionPolicy);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.StatePath, "run.sqlite")));
        Assert.IsFalse(File.Exists(configuration.CampaignLedgerPath));
    }

    [TestMethod]
    public void DeserializeUnknownConfigurationFieldRejectsConfiguration()
    {
        using RunFixture fixture = new();
        System.Text.Json.Nodes.JsonObject payload = Json.SerializePayload(fixture.Configuration);
        payload["actor"] = "owner";

        JsonException error = Assert.ThrowsExactly<JsonException>(() =>
            JsonSerializer.Deserialize<RunConfiguration>(payload.ToJsonString(), Json.Options)
        );

        Assert.Contains("actor", error.Message);
    }
}
