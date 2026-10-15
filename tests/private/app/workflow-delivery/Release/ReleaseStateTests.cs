using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class ReleaseStateTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CompleteReadbackSatisfiesRequestWhilePreservingCommandOutcome(bool acknowledged)
    {
        PreparedRelease prepared = Prepare();
        NativeCommandResult command = Command(acknowledged);

        ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared, Observe(prepared),
            [new("package", command)]);

        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.State);
        Assert.AreEqual(prepared.Subject, result.Subject);
        Assert.AreSame(command, Assert.ContainsSingle(result.Uploads).Command);
        Assert.IsEmpty(result.Reasons);
        Assert.AreEqual(acknowledged, result.Uploads[0].Command.Succeeded);
    }

    [TestMethod]
    public void InitiallyExactUsesSameCompletePredicateWithoutAnUpload()
    {
        PreparedRelease prepared = Prepare();

        ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared, Observe(prepared), []);

        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.State);
        Assert.IsEmpty(result.Uploads);
        Assert.IsEmpty(result.Reasons);
    }

    [TestMethod]
    [DataRow("Absent", "Unsatisfied")]
    [DataRow("Conflict", "Unsatisfied")]
    [DataRow("Unknown", "Unknown")]
    public void SuccessfulUploadCannotReplaceCompleteDestinationState(string stateName,
        string expectedName)
    {
        PreparedRelease prepared = Prepare();

        ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared,
            Observe(prepared, Enum.Parse<ReleaseFileState>(stateName)),
            [new("package", Command(true))]);

        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedName), result.State);
        Assert.IsTrue(Assert.ContainsSingle(result.Uploads).Command.Succeeded);
        Assert.IsNotEmpty(result.Reasons);
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Missing")]
    [DataRow("Cancelled")]
    [DataRow("Skipped")]
    [DataRow("TimedOut")]
    public void ExactFilesCannotReplaceRequiredPreparationOrRemoteChecks(
        string statusName)
    {
        ReleaseCheckStatus status = Enum.Parse<ReleaseCheckStatus>(statusName);
        PreparedRelease prepared = Prepare();
        ReleaseObservation observation = Observe(prepared);
        ReleaseOutcome local = ReleaseCompletion.Evaluate(prepared with
        { Checks = [new("native-consumer", status)] }, observation, []);
        ReleaseOutcome remote = ReleaseCompletion.Evaluate(prepared, observation with
        { Checks = [new("visibility", status)] }, []);

        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, local.State);
        Assert.Contains("native-consumer", Assert.ContainsSingle(local.Reasons));
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, remote.State);
        Assert.Contains("visibility", Assert.ContainsSingle(remote.Reasons));
    }

    [TestMethod]
    public void MissingObservationPreservesUncertainEffectsAndRawFailedCommand()
    {
        PreparedRelease prepared = Prepare();
        NativeCommandResult command = Command(false);

        ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared, null,
            [new("package", command)]);

        Assert.AreEqual(ReleaseBusinessState.Unknown, result.State);
        Assert.Contains("effects remain unknown", Assert.ContainsSingle(result.Reasons));
        Assert.AreSame(command, Assert.ContainsSingle(result.Uploads).Command);
    }

    [TestMethod]
    public void AmbiguousWheelUploadDoesNotPermitLaterSdistEvenWithCompleteReadback()
    {
        PreparedRelease prepared = Prepare();
        prepared = prepared with
        {
            Files = [prepared.Files[0], prepared.Files[0] with
            { Output = "sdist", Kind = "python-sdist", Path = "original/source.tar.gz" }]
        };
        ReleaseObservation exact = Observe(prepared);

        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCompletion.Evaluate(prepared,
            exact, [new("package", Command(false)), new("sdist", Command(true))]));

        ReleaseOutcome confirmed = ReleaseCompletion.Evaluate(prepared, exact,
            [new("package", Command(false))]);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, confirmed.State);
        Assert.AreEqual("package", Assert.ContainsSingle(confirmed.Uploads).Output);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("other-name")]
    [DataRow("other-version")]
    [DataRow("other-request")]
    [DataRow("other-bytes")]
    public void RejectsMismatchedOrIncompleteObservation(string defect)
    {
        PreparedRelease prepared = Prepare();
        ReleaseObservation observation = Observe(prepared);
        ReleaseFileObservation file = observation.Files[0];
        observation = defect switch
        {
            "duplicate" => observation with { Files = [file, file] },
            "missing" => observation with { Files = [] },
            "other-name" => observation with { Files = [file with { Name = "other" }] },
            "other-version" => observation with { Files = [file with { Version = "2.0.0" }] },
            "other-request" => observation with
            { Subject = observation.Subject with { Revision = new('b', 40) } },
            _ => observation with { Files = [file with { Sha256 = new('b', 64) }] },
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseCompletion.Evaluate(prepared, observation, []));
    }

    [TestMethod]
    [DataRow("Exact", true, 0)]
    [DataRow("Absent", true, 1)]
    [DataRow("Conflict", false, 0)]
    [DataRow("Unknown", false, 0)]
    public void DevelopmentDryRunOnlyDescribesSupportedHypotheticalWrites(
        string stateName, bool ready, int writes)
    {
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        PreparedRelease prepared = Prepare() with
        { Subject = Prepare().Subject with { DryRun = true, SelectedRef = "refs/heads/feature" } };

        ReleaseDryRun result = ReleaseCompletion.Describe(prepared, Observe(prepared, state),
            state == ReleaseFileState.Absent ? ["package"] : []);

        Assert.AreEqual(ready, result.Ready);
        Assert.HasCount(writes, result.HypotheticalWrites);
        Assert.IsTrue(result.Subject.DryRun);
        Assert.AreEqual("refs/heads/feature", result.Subject.SelectedRef);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseCompletion.Evaluate(prepared, Observe(prepared), []));
    }

    [TestMethod]
    public void UnsupportedPartialSetCannotBecomeHypotheticalPublication()
    {
        PreparedRelease prepared = Prepare() with
        { Subject = Prepare().Subject with { DryRun = true } };

        ReleaseDryRun result = ReleaseCompletion.Describe(prepared,
            Observe(prepared, ReleaseFileState.Absent), []);

        Assert.IsFalse(result.Ready);
        Assert.IsEmpty(result.HypotheticalWrites);
        Assert.Contains("missing set", Assert.ContainsSingle(result.Reasons));
    }

    [TestMethod]
    [DataRow("buddy", "refs/heads/feature", false, true)]
    [DataRow("buddy", "refs/heads/", false, false)]
    [DataRow("buddy", "refs/tags/v1", false, false)]
    [DataRow("official", "refs/heads/main", false, true)]
    [DataRow("official", "refs/heads/feature", false, false)]
    [DataRow("official", "refs/tags/v1", false, false)]
    [DataRow("official", "refs/tags/v1", true, true)]
    [DataRow("buddy", "refs/tags/v1", true, true)]
    public void LiveRefEligibilityHasOneDefinition(string channel, string selectedRef,
        bool dryRun, bool eligible)
    {
        ReleaseSubject subject = Prepare().Subject with
        { Channel = channel, SelectedRef = selectedRef, DryRun = dryRun };
        if (eligible) ReleasePolicy.RequireEligible(subject);
        else Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleasePolicy.RequireEligible(subject));
    }

    private static PreparedRelease Prepare() => new(
        new(new('a', 40), "refs/heads/release/example", "example", "product/release.yml",
            "official", "npmjs", false),
        [new("native", "package", "npm-tarball", "@example/package", "1.0.0",
            "original/package.tgz", 128, new('a', 64))],
        ["native-consumer"], [new("native-consumer", ReleaseCheckStatus.Passed)]);

    private static ReleaseObservation Observe(PreparedRelease prepared,
        ReleaseFileState state = ReleaseFileState.Exact) => new(prepared.Subject,
        prepared.Files.Select(file => new ReleaseFileObservation(file.Output, file.Name,
            file.Version, state, state == ReleaseFileState.Exact ? file.Size : null,
            state == ReleaseFileState.Exact ? file.Sha256 : null)).ToArray(),
        ["visibility"], [new("visibility", ReleaseCheckStatus.Passed)]);

    private static NativeCommandResult Command(bool acknowledged) => new(
        NativeTermination.Exited, acknowledged ? 0 : 7, "original output", "diagnostic", 1,
        null);
}
