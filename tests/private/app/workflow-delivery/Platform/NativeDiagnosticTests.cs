using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Platform;

[TestClass]
public sealed class NativeDiagnosticTests
{
    [TestMethod]
    public void TailKeepsShortTextAndMarksTruncationWithoutSplittingSurrogatePairs()
    {
        Assert.AreEqual("short stderr", NativeDiagnostic.Tail("  short stderr\n"));
        Assert.AreEqual("", NativeDiagnostic.Tail(null));

        string kept = new string('x', NativeDiagnostic.TailLength - 4) + "tail";
        Assert.AreEqual("[truncated] " + kept, NativeDiagnostic.Tail("omitted head" + kept));

        // The cut lands on the low half of a pair; the whole pair is dropped, never half.
        string pair = "\uD83D\uDE00";
        string after = new string('y', NativeDiagnostic.TailLength - 1);
        string tail = NativeDiagnostic.Tail("head" + pair + after);
        Assert.AreEqual("[truncated] " + after, tail);
        Assert.IsFalse(tail.Any(char.IsSurrogate));
    }
}
