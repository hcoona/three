using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Platform;

[TestClass]
public sealed class GitHubComparisonTests
{
    private static readonly string Basis = new('a', 40);
    private static readonly string Candidate = new('b', 40);
    private static readonly string SourceHead = new('c', 40);

    [TestMethod]
    public void PullRequestUsesTestedMergeAndNativeBase()
    {
        using JsonDocument payload = JsonDocument.Parse($$$"""
            {"action":"synchronize","pull_request":{"base":{"sha":"{{{Basis}}}"},
             "head":{"sha":"{{{SourceHead}}}"}},"repository":{"unrelated":true}}
            """);

        CiComparison result = GitHubComparison.Resolve("pull_request", payload.RootElement,
            Candidate);

        Assert.AreEqual(new CiComparison(Basis, Candidate, false), result);
        Assert.AreNotEqual(SourceHead, result.Candidate);
    }

    [TestMethod]
    public void MergeGroupUsesWholeNativeGroup()
    {
        using JsonDocument payload = JsonDocument.Parse($$$"""
            {"action":"checks_requested","merge_group":{"base_sha":"{{{Basis}}}",
             "head_sha":"{{{Candidate}}}","head_ref":"refs/heads/gh-readonly-queue/main/group"}}
            """);

        CiComparison result = GitHubComparison.Resolve("merge_group", payload.RootElement,
            Candidate);

        Assert.AreEqual(new CiComparison(Basis, Candidate, false), result);
    }

    [TestMethod]
    [DataRow("destroyed", false)]
    [DataRow("checks_requested", true)]
    public void MergeGroupRejectsUnsupportedActionOrConflictingCandidate(string action,
        bool conflict)
    {
        using JsonDocument payload = JsonDocument.Parse($$$"""
            {"action":"{{{action}}}","merge_group":{"base_sha":"{{{Basis}}}",
             "head_sha":"{{{(conflict ? SourceHead : Candidate)}}}"}}
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => GitHubComparison.Resolve(
            "merge_group", payload.RootElement, Candidate));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PushUsesNativeEndpointsIncludingForcePush(bool forced)
    {
        using JsonDocument payload = JsonDocument.Parse($$$"""
            {"before":"{{{Basis}}}","after":"{{{Candidate}}}","deleted":false,
             "forced":{{{forced.ToString().ToLowerInvariant()}}},"commits":[]}
            """);

        CiComparison result = GitHubComparison.Resolve("push", payload.RootElement, Candidate);

        Assert.AreEqual(new CiComparison(Basis, Candidate, false), result);
    }

    [TestMethod]
    [DataRow("created")]
    [DataRow("deleted")]
    [DataRow("conflicting")]
    public void PushRejectsMissingBasisDeletedOrConflictingCandidate(string defect)
    {
        string before = defect == "created" ? new('0', 40) : Basis;
        string after = defect == "conflicting" ? SourceHead : Candidate;
        string deleted = defect == "deleted" ? "true" : "false";
        using JsonDocument payload = JsonDocument.Parse($$$"""
            {"before":"{{{before}}}","after":"{{{after}}}","deleted":{{{deleted}}}}
            """);

        Assert.ThrowsExactly<InvalidDataException>(() => GitHubComparison.Resolve(
            "push", payload.RootElement, Candidate));
    }

    [TestMethod]
    public void DispatchAloneSelectsExplicitFull()
    {
        using JsonDocument payload = JsonDocument.Parse("{\"inputs\":{\"unrelated\":true}}");

        CiComparison result = GitHubComparison.Resolve("workflow_dispatch", payload.RootElement,
            Candidate);

        Assert.AreEqual(new CiComparison(Candidate, Candidate, true), result);
    }

    [TestMethod]
    [DataRow(40)]
    [DataRow(64)]
    public void NativeFullIdentitiesNormalizeCase(int length)
    {
        string basis = new('A', length);
        string candidate = new('B', length);
        using JsonDocument payload = JsonDocument.Parse($$$"""
            {"before":"{{{basis}}}","after":"{{{candidate.ToLowerInvariant()}}}","deleted":false}
            """);

        CiComparison result = GitHubComparison.Resolve("push", payload.RootElement, candidate);

        Assert.AreEqual(new CiComparison(basis.ToLowerInvariant(), candidate.ToLowerInvariant(),
            false), result);
    }

    [TestMethod]
    [DataRow("HEAD")]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("bbbbbbb")]
    [DataRow("0000000000000000000000000000000000000000")]
    [DataRow("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbg")]
    public void InvalidTestedIdentityFails(string candidate)
    {
        using JsonDocument payload = JsonDocument.Parse("{}");

        Assert.ThrowsExactly<InvalidDataException>(() => GitHubComparison.Resolve(
            "workflow_dispatch", payload.RootElement, candidate));
    }

    [TestMethod]
    [DataRow("pull_request", "{}")]
    [DataRow("pull_request", "{\"pull_request\":null}")]
    [DataRow("pull_request", "{\"pull_request\":{\"base\":[]}}")]
    [DataRow("pull_request", "{\"pull_request\":{\"base\":{}}}")]
    [DataRow("pull_request", "{\"pull_request\":{\"base\":{\"sha\":null}}}")]
    [DataRow("pull_request", "{\"pull_request\":{\"base\":{\"sha\":5}}}")]
    [DataRow("pull_request", "{\"pull_request\":{\"base\":{\"sha\":\"main\"}}}")]
    [DataRow("merge_group", "{\"action\":false}")]
    [DataRow("merge_group", "{\"action\":\"checks_requested\",\"merge_group\":"
        + "{\"base_sha\":\"$basis\",\"head_sha\":null}}")]
    [DataRow("merge_group", "{\"action\":\"checks_requested\",\"merge_group\":"
        + "{\"head_sha\":\"$candidate\"}}")]
    [DataRow("push", "{\"before\":\"$basis\",\"after\":\"$candidate\"}")]
    [DataRow("push", "{\"before\":\"$basis\",\"after\":\"$candidate\",\"deleted\":\"false\"}")]
    [DataRow("push", "{\"after\":\"$candidate\",\"deleted\":false}")]
    [DataRow("push", "{\"before\":\"$basis\",\"after\":[],\"deleted\":false}")]
    [DataRow("workflow_dispatch", "null")]
    [DataRow("workflow_dispatch", "[]")]
    public void InvalidRelevantNativeFieldsFail(string eventName, string nativeJson)
    {
        using JsonDocument payload = JsonDocument.Parse(nativeJson.Replace("$basis", Basis,
            StringComparison.Ordinal).Replace("$candidate", Candidate, StringComparison.Ordinal));

        Assert.ThrowsExactly<InvalidDataException>(() => GitHubComparison.Resolve(eventName,
            payload.RootElement, Candidate));
    }

    [TestMethod]
    [DataRow("pull_request_target")]
    [DataRow("schedule")]
    public void UnsupportedEventFails(string eventName)
    {
        using JsonDocument payload = JsonDocument.Parse("{}");

        Assert.ThrowsExactly<InvalidDataException>(() => GitHubComparison.Resolve(eventName,
            payload.RootElement, Candidate));
    }
}
