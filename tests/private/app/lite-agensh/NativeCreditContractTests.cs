using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class NativeCreditContractTests
{
    [TestMethod]
    [DataRow("different-version")]
    [DataRow("unknown-version")]
    [DataRow("unofficial-documentation")]
    [DataRow("unknown-information-command")]
    [DataRow("unknown-report-scope")]
    [DataRow("noncumulative-session-usage")]
    [DataRow("relative-usage-pointer")]
    public void VerifyVersionUnreviewableOrChangedEvidenceRejectsContract(string scenario)
    {
        NativeCreditContract contract = CreateScriptedContract();
        JsonObject initialize = new()
        {
            ["agentInfo"] = new JsonObject { ["version"] = "self-authored-acp-1" },
        };
        switch (scenario)
        {
            case "different-version":
                initialize["agentInfo"]!["version"] = "self-authored-acp-2";
                break;
            case "unknown-version":
                initialize.Remove("agentInfo");
                break;
            case "unofficial-documentation":
                contract = contract with
                {
                    UsageReportDocumentation = "https://example.invalid/usage",
                };
                break;
            case "unknown-information-command":
                contract = contract with { InformationalCommand = "please estimate credits" };
                break;
            case "unknown-report-scope":
                contract = contract with { ReportUsageScope = "unknown" };
                break;
            case "noncumulative-session-usage":
                contract = contract with { UsageIsSessionCumulative = false };
                break;
            case "relative-usage-pointer":
                contract = contract with { SessionUsagePointer = "used" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            contract.VerifyVersion(initialize)
        );

        Assert.AreEqual("unavailable", error.Code);
    }

    [TestMethod]
    [DataRow("""{"used":12}""")]
    [DataRow("""{"limit":60}""")]
    [DataRow("""{"limit":59,"used":12}""")]
    [DataRow("""{"limit":"60","used":12}""")]
    [DataRow("""{"limit":60,"used":"unknown"}""")]
    [DataRow("""{"limit":60,"used":-1}""")]
    public void VerifySessionUnknownLimitOrUsageRejectsEvidence(string payload)
    {
        NativeCreditContract contract = CreateScriptedContract();

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            contract.VerifySession(JsonNode.Parse(payload)!, 60)
        );

        Assert.AreEqual("credit_unknown", error.Code);
    }

    [TestMethod]
    [DataRow("12.5", "12.5")]
    [DataRow("67.25", "67.25")]
    public void VerifySessionDocumentedNestedPointersReadActualUsage(
        string reported,
        string expected
    )
    {
        NativeCreditContract contract = CreateScriptedContract() with
        {
            AppliedLimitPointer = "/limits/native~1soft",
            SessionUsagePointer = "/sessions/0/usage~0credits",
        };
        JsonObject initialize = new()
        {
            ["agentInfo"] = new JsonObject { ["version"] = "self-authored-acp-1" },
        };
        JsonNode information = JsonNode.Parse(
            """{"limits":{"native/soft":60,"other":999},"sessions":["""
                + $$"""{"usage~credits":{{reported}}},{"usage~credits":999}]}"""
        )!;

        contract.VerifyVersion(initialize);
        decimal actual = contract.VerifySession(information, 60);

        Assert.AreEqual(
            decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            actual
        );
    }

    [TestMethod]
    public void ReadFinalReportReportedUsageRetainsOvershootAndBindsSession()
    {
        using RunFixture fixture = new();
        NativeCreditContract contract = CreateScriptedContract();
        string reportPath = Path.Combine(fixture.RootPath, "actual-report.json");
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 120);
        CreditReservation reservation = ledger.Reserve("run-a", "worker1", 30, 60, reportPath);
        ledger.MarkStarted(reservation.Id);
        ledger.AttachSession(reservation.Id, "session-a");
        File.WriteAllText(
            reportPath,
            """{"sessionId":"another-session","usage":{"aiCredits":41.5}}"""
        );

        OperationException mismatch = Assert.ThrowsExactly<OperationException>(() =>
            contract.ReadFinalReport(reportPath, "session-a")
        );
        Assert.AreEqual("credit_unknown", mismatch.Code);
        CreditReservation before = Assert.ContainsSingle(ledger.Reservations());
        Assert.AreEqual("active", before.State);
        Assert.IsNull(before.Actual);

        File.WriteAllText(reportPath, """{"sessionId":"session-a","usage":{"aiCredits":41.5}}""");
        (decimal actual, string identity) = contract.ReadFinalReport(reportPath, "session-a");
        ledger.Reconcile(reservation.Id, actual, identity);
        ledger.Reconcile(reservation.Id, actual, identity);

        CreditReservation retained = Assert.ContainsSingle(
            new CreditLedger(fixture.Configuration.CampaignLedgerPath, 120).Reservations()
        );
        Assert.AreEqual(41.5m, retained.Actual);
        Assert.AreEqual("reconciled", retained.State);
        Assert.AreEqual("session-a", retained.SessionId);
        JsonObject status = ledger.Status("run-a", 60);
        Assert.AreEqual(41.5m, status["campaignCommitted"]!.GetValue<decimal>());
        Assert.AreEqual(11.5m, status["overshoot"]!.GetValue<decimal>());
    }

    [TestMethod]
    [DataRow("""{"sessionId":"session-a","usage":{}}""")]
    [DataRow("""{"sessionId":"session-a","usage":{"aiCredits":"unknown"}}""")]
    public void ReadFinalReportUnknownUsageCannotBecomeZero(string report)
    {
        using RunFixture fixture = new();
        string path = Path.Combine(fixture.RootPath, "unknown-report.json");
        File.WriteAllText(path, report);

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            CreateScriptedContract().ReadFinalReport(path, "session-a")
        );

        Assert.AreEqual("credit_unknown", error.Code);
    }

    private static NativeCreditContract CreateScriptedContract() =>
        new()
        {
            // These self-authored fields exercise evidence handling only. They are
            // not an approved descriptor for any installed native agent or account.
            AgentVersion = "self-authored-acp-1",
            EvidenceReference = "credential-free scripted contract test; not native observation",
            AppliedLimitDocumentation =
                "https://docs.github.com/en/copilot/how-tos/"
                + "copilot-cli/use-copilot-cli/set-session-limit",
            UsageReportDocumentation = "https://agentclientprotocol.com/protocol/session-setup",
            InformationalCommand = "usage",
            AppliedLimitPointer = "/limit",
            SessionUsagePointer = "/used",
            ReportCreditsPointer = "/usage/aiCredits",
            ReportSessionPointer = "/sessionId",
            ReportUsageScope = "session-cumulative",
            UsageIsSessionCumulative = true,
        };
}
