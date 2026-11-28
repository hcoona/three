using System.Collections.Immutable;
using WorkflowDelivery.Core;
using WorkflowDelivery.Engine.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PublicationWindowTests(TestContext context)
{
    [TestMethod]
    public void MissingVerifierIsUnknownButAnIncompleteProofCannotStandInForIt()
    {
        var id = new OperationId("create", "original");
        var complete = new PublicationEvidence(true,
            ImmutableDictionary<OperationId, OperationEvidence>.Empty.Add(
                id, OperationEvidence.Established));
        var target = PublicationWindow.Bind(
            new([new(id)], DestinationVersionRead.BeforeGate), complete, null);

        var missing = PublicationWindow.Complete(target, null);
        Assert.AreEqual(PublicationStatus.Unknown, missing.State);
        Assert.IsFalse(missing.Attempted);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PublicationWindow.Complete(
                target, complete with { Operations = complete.Operations.Clear() }));
    }

    [TestMethod]
    [DataRow(true, false, "Unknown", true)]
    [DataRow(false, true, "Unsatisfied", null)]
    public void IndependentReceiptLossPreservesOtherPortFacts(bool anchorIssued,
        bool anchorConflict, string combinedState, bool? attempted)
    {
        var id = new OperationId("create", "same-local-id");
        var profile = new PublicationProfile([new(id)], DestinationVersionRead.BeforeGate);
        PublicationEvidence Read(OperationEvidence state) =>
            new(true, ImmutableDictionary<OperationId, OperationEvidence>.Empty.Add(id, state));
        var absent = Read(OperationEvidence.Missing);
        var exact = Read(OperationEvidence.Established);
        var primary = PublicationWindow.Bind(profile, absent, null);
        var anchor = PublicationWindow.Bind(profile, anchorIssued ? absent : exact, anchorIssued
            ? new(absent, [new(id, true)], null, false) : null);

        var (first, second, combined) = PublicationWindow.Resolve(primary, exact, anchor,
            anchorConflict ? Read(OperationEvidence.Conflict) : exact);

        Assert.AreEqual(PublicationStatus.Unknown, first.State);
        Assert.IsNull(first.Attempted);
        Assert.AreEqual(anchorIssued, second!.Attempted);
        Assert.AreEqual(Enum.Parse<PublicationStatus>(combinedState), combined.State);
        Assert.AreEqual(attempted, combined.Attempted);
    }

    [TestMethod]
    [DataRow(true, false, true, "second,third")]
    [DataRow(true, false, false, "second")]
    [DataRow(true, true, true, "third")]
    [DataRow(false, true, true, "")]
    public async Task ChainUsesOnlyFreshOrAcknowledgedPrerequisites(bool firstPresent,
        bool secondPresent, bool acknowledge, string expectedCalls)
    {
        var first = new OperationId("create", "first");
        var second = new OperationId("create", "second");
        var third = new OperationId("create", "third");
        var profile = new PublicationProfile([new(first), new(second, first), new(third, second)],
            DestinationVersionRead.BeforeGate);
        var reviewed = profile.Review(Evidence(false, false));
        var calls = new List<string>();

        await PublicationWindow.RunAsync(
            reviewed, _ => Task.FromResult(Evidence(firstPresent, secondPresent)),
            (op, _) =>
            {
                calls.Add(op.Target);
                return Task.FromResult(new OperationAttempt(op, acknowledge));
            }, context.CancellationToken);

        Assert.AreEqual(expectedCalls, string.Join(",", calls));

        PublicationEvidence Evidence(bool hasFirst, bool hasSecond) => new(true,
            new Dictionary<OperationId, OperationEvidence>
            {
                [first] = hasFirst ? OperationEvidence.Established : OperationEvidence.Missing,
                [second] = hasSecond ? OperationEvidence.Established : OperationEvidence.Missing,
                [third] = OperationEvidence.Missing,
            }.ToImmutableDictionary());
    }
}
