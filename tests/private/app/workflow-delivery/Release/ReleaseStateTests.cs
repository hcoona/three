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
    public void AmbiguousUploadDoesNotPermitLaterWritesEvenWithCompleteReadback()
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
    [DataRow(0)]
    [DataRow(2)]
    public void RejectsMissingOrDuplicatedFileObservation(int copies) =>
        RejectObservation(observation => observation with
        { Files = Enumerable.Repeat(observation.Files[0], copies).ToArray() });

    [TestMethod]
    [DataRow("other", null, null)]
    [DataRow(null, "2.0.0", null)]
    [DataRow(null, null, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public void RejectsChangedFileObservation(string? name, string? version, string? sha256) =>
        RejectObservation(observation =>
        {
            ReleaseFileObservation file = observation.Files[0];
            return observation with
            {
                Files = [file with
                { Name = name ?? file.Name, Version = version ?? file.Version,
                    Sha256 = sha256 ?? file.Sha256 }]
            };
        });

    [TestMethod]
    public void RejectsObservationForAnotherRevision() =>
        RejectObservation(observation => observation with
        { Subject = observation.Subject with { Revision = new('b', 40) } });

    private static void RejectObservation(Func<ReleaseObservation, ReleaseObservation> change)
    {
        PreparedRelease prepared = Prepare();
        ReleaseObservation observation = change(Observe(prepared));

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
    [DataRow(false)]
    [DataRow(true)]
    public void PartialSetNeedsDestinationSupportForExactlyTheMissingFiles(bool supported)
    {
        PreparedRelease prepared = Prepare();
        ReleaseFile second = prepared.Files[0] with
        { Output = "symbols", Kind = "debug-symbols", Path = "original/symbols.zip" };
        prepared = prepared with
        {
            Subject = prepared.Subject with { DryRun = true },
            Files = [prepared.Files[0], second]
        };
        ReleaseObservation observation = Observe(prepared);
        observation = observation with
        {
            Files = [observation.Files[0], observation.Files[1] with
                { State = ReleaseFileState.Absent, Size = null, Sha256 = null }]
        };

        ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observation,
            supported ? [second.Output] : []);

        Assert.AreEqual(supported, result.Ready);
        if (supported)
        {
            Assert.AreEqual(second.Output, Assert.ContainsSingle(result.HypotheticalWrites));
            Assert.IsEmpty(result.Reasons);
        }
        else
        {
            Assert.IsEmpty(result.HypotheticalWrites);
            Assert.Contains("missing set", Assert.ContainsSingle(result.Reasons));
        }
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

    [TestMethod]
    public void DryRunAndLiveCompletionCannotBeInterchanged()
    {
        PreparedRelease live = Prepare();
        PreparedRelease dry = live with { Subject = live.Subject with { DryRun = true } };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseCompletion.Evaluate(dry, Observe(dry), []));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseCompletion.Describe(live, Observe(live), []));
    }

    [TestMethod]
    [DataRow("Exited", "Failed")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("Cancelled", "Cancelled")]
    public async Task QualificationRetainsTheNativeFailureAndSkipsDependentChecks(
        string termination, string status)
    {
        NativeCommandResult failed = Command(false) with
        { Termination = Enum.Parse<NativeTermination>(termination) };
        var command = new NativeCommand("controlled", ".", ["build"], 30);
        var qualification = new ReleaseQualification((_, _) => Task.FromResult(failed),
            CancellationToken.None);

        await qualification.CheckAsync("inputs", () => Task.CompletedTask);
        await qualification.CheckAsync("build", () => qualification.RunAsync(command));
        await qualification.CheckAsync("consumer", () =>
            throw new AssertFailedException("Unqualified original reached the consumer."));

        CollectionAssert.AreEqual(new[]
        {
            new ReleaseCheck("inputs", ReleaseCheckStatus.Passed),
            new ReleaseCheck("build", Enum.Parse<ReleaseCheckStatus>(status)),
            new ReleaseCheck("consumer", ReleaseCheckStatus.Skipped),
        }, qualification.Checks);
        Assert.AreEqual(new ReleaseCommand("build", command, failed),
            Assert.ContainsSingle(qualification.Commands));
        Assert.Contains("build", Assert.ContainsSingle(qualification.Errors));
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
