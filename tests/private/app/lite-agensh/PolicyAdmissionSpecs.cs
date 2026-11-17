using System.Text;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class PolicyAdmissionSpecs
{
    [TestMethod]
    [DataRow("FACT", 99, 99)]
    [DataRow("OBSERVED", 100, 100)]
    [DataRow("FAIL", 101, 100)]
    [DataRow("PATCH_SUMMARY", 299, 299)]
    [DataRow("PATCH_SUMMARY", 300, 300)]
    [DataRow("PATCH_SUMMARY", 301, 300)]
    public void AdmitRuneCapBoundaryPreservesCompleteGist(
        string type,
        int length,
        int expectedLength
    )
    {
        const string prefix = "evidence=passed | ";
        string gist = prefix + new string('x', length - prefix.Length);

        string admitted = BoardPolicy.Admit(type, " \t" + gist + "\r\n");

        Assert.AreEqual(prefix + new string('x', expectedLength - prefix.Length), admitted);
        Assert.AreEqual(expectedLength, admitted.EnumerateRunes().Count());
    }

    [TestMethod]
    [DataRow("FACT", 100)]
    [DataRow("PATCH_SUMMARY", 300)]
    public void AdmitSurrogateAtLastRuneDoesNotSplitOrCountUtf16Units(string type, int cap)
    {
        const string prefix = "evidence=verified | ";
        string before = prefix + new string('a', cap - prefix.Length - 1);

        string admitted = BoardPolicy.Admit(type, before + "\U0001F680" + "discard");

        Assert.AreEqual(before + "\U0001F680", admitted);
        Assert.AreEqual(cap, admitted.EnumerateRunes().Count());
        Assert.AreEqual(cap + 1, admitted.Length);
    }

    [TestMethod]
    [DataRow("fact", "a real observation", "Unknown board type.")]
    [DataRow("UNKNOWN", "a real observation", "Unknown board type.")]
    [DataRow("FACT", " \t ", "Board admission rejected an empty/placeholder gist.")]
    [DataRow("OBSERVED", " NONE ", "Board admission rejected an empty/placeholder gist.")]
    [DataRow("FAIL", "(none)", "Board admission rejected an empty/placeholder gist.")]
    [DataRow("CLAIM", "-", "Board admission rejected an empty/placeholder gist.")]
    [DataRow("FACT", "\u2014", "Board admission rejected an empty/placeholder gist.")]
    [DataRow("PATCH_SUMMARY", "fixed code", "Board admission rejected placeholder patch evidence.")]
    [DataRow(
        "PATCH_SUMMARY",
        "evidence=should pass",
        "Board admission rejected placeholder patch evidence."
    )]
    [DataRow(
        "PATCH_SUMMARY",
        "evidence=pending: run",
        "Board admission rejected placeholder patch evidence."
    )]
    public void AdmitInvalidGistRejectsWithSpecificReason(string type, string gist, string message)
    {
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            BoardPolicy.Admit(type, gist)
        );

        Assert.AreEqual("invalid_arguments", error.Code);
        Assert.AreEqual(message, error.Message);
    }

    [TestMethod]
    [DataRow("evidence=passed parser checks", true)]
    [DataRow(" Evidence=CONFIRMED output | file=parser.py", true)]
    [DataRow("evidence=now returns 42", true)]
    [DataRow("evidence=looks verified", false)]
    [DataRow("evidence=unverified", false)]
    [DataRow("evidence=compiled locally", false)]
    [DataRow("evidence=compass", false)]
    [DataRow("text=passed | evidence= ", false)]
    public void IsVerifiedCheckpointRequiresSolidNonWeakEvidence(string content, bool expected)
    {
        Assert.AreEqual(expected, BoardPolicy.IsVerifiedCheckpoint(content));
    }

    [TestMethod]
    public void AdmitNonPlaceholderEvidenceIsNotAutomaticallyVerified()
    {
        string admitted = BoardPolicy.Admit("PATCH_SUMMARY", " evidence=compiled locally ");

        Assert.AreEqual("evidence=compiled locally", admitted);
        Assert.IsFalse(BoardPolicy.IsVerifiedCheckpoint(admitted));
    }

    [TestMethod]
    [DataRow(" G0012 ", 12L)]
    [DataRow("G9223372036854775807", long.MaxValue)]
    public void ParseLabelPositiveCanonicalPrefixAcceptsDecimalId(string label, long expected)
    {
        Assert.AreEqual<long?>(expected, BoardPolicy.ParseLabel(label));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("G0")]
    [DataRow("g12")]
    [DataRow("G-1")]
    [DataRow("G+12")]
    [DataRow("G1 2")]
    [DataRow("G9223372036854775808")]
    [DataRow("G\u0661")]
    public void ParseLabelInvalidOrOverflowingIdentityReturnsNoId(string label)
    {
        Assert.IsNull(BoardPolicy.ParseLabel(label));
    }

    [TestMethod]
    public void NormalizeWhitespaceAndCasePreservesLiteralWords()
    {
        Assert.AreEqual("Parser FAIL #12", BoardPolicy.Normalize(" \tParser\r\n FAIL  #12 \n"));
        Assert.AreEqual(string.Empty, BoardPolicy.Normalize("\t \r\n"));
    }

    [TestMethod]
    [DataRow("task #12, then #123", 12L, true)]
    [DataRow("task #0012", 12L, true)]
    [DataRow("task #123", 12L, false)]
    [DataRow("path/#12 word#12 #12suffix", 12L, false)]
    [DataRow("#9223372036854775808", 12L, false)]
    public void CitesTaskRecognizesWholeStandaloneNumericReference(
        string text,
        long task,
        bool expected
    )
    {
        Assert.AreEqual(expected, BoardPolicy.CitesTask(text, task));
    }

    [TestMethod]
    public void SemanticKeyStrongSortedAnchorsSeparatesTypeAndLocation()
    {
        const string content = "b.py a.py a.py def parse line 002 line 10 ValueError test_parse";
        string expected = string.Join(
            '\0',
            "FACT",
            "a.py|b.py",
            "parse",
            "10|2",
            "ValueError",
            "test_parse"
        );

        Assert.AreEqual(expected, BoardPolicy.SemanticKey("FACT", content));
        Assert.AreEqual(
            expected,
            BoardPolicy.SemanticKey(
                "FACT",
                "test_parse ValueError line 10 line 2 def parse a.py b.py"
            )
        );
        Assert.AreNotEqual(expected, BoardPolicy.SemanticKey("FAIL", content));
        Assert.AreNotEqual(expected, BoardPolicy.SemanticKey("FACT", content + " c.py"));
        Assert.IsNull(BoardPolicy.SemanticKey("FACT", "a.py deflection"));
    }

    [TestMethod]
    public void CompileQueryBooleanOperatorsKeepDistinctOrderedLiteralParameters()
    {
        var query = BoardPolicy.CompileQuery("alpha | (beta & gamma)");

        Assert.HasCount(3, query.Parameters);
        Assert.AreEqual(("$term0", (object?)"alpha"), query.Parameters[0]);
        Assert.AreEqual(("$term1", (object?)"beta"), query.Parameters[1]);
        Assert.AreEqual(("$term2", (object?)"gamma"), query.Parameters[2]);
        Assert.DoesNotContain("alpha", query.Sql);
        Assert.DoesNotContain("gamma", query.Sql);
    }

    [TestMethod]
    [DataRow("alpha OR beta")]
    [DataRow("' OR 1=1 --")]
    [DataRow("\"beta AND gamma\"")]
    [DataRow("needle'; DROP TABLE board; --")]
    public void CompileQueryBooleanWordsAndSqlPunctuationRemainOneBoundValue(string literal)
    {
        var query = BoardPolicy.CompileQuery(literal);

        Assert.HasCount(1, query.Parameters);
        Assert.AreEqual(("$term0", (object?)literal), query.Parameters[0]);
        Assert.DoesNotContain(literal, query.Sql);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("alpha &")]
    [DataRow("| alpha")]
    [DataRow("(alpha")]
    [DataRow("alpha)")]
    [DataRow("alpha || beta")]
    public void CompileQueryMissingOperandOrUnbalancedGroupRejects(string text)
    {
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            BoardPolicy.CompileQuery(text)
        );

        Assert.AreEqual("invalid_arguments", error.Code);
        Assert.AreEqual(
            "Invalid board query: use literal terms, &, | and balanced parentheses.",
            error.Message
        );
    }

    [TestMethod]
    [DataRow(50, true)]
    [DataRow(51, false)]
    public void CompileQueryLiteralTermBudgetIsFinite(int terms, bool accepted)
    {
        string input = string.Join(" | ", Enumerable.Range(1, terms).Select(i => $"item{i}"));
        if (accepted)
        {
            var query = BoardPolicy.CompileQuery(input);
            Assert.HasCount(50, query.Parameters);
            Assert.AreEqual(("$term49", (object?)"item50"), query.Parameters[49]);
            return;
        }

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            BoardPolicy.CompileQuery(input)
        );
        Assert.AreEqual("invalid_arguments", error.Code);
        Assert.AreEqual("Board queries allow at most 50 literal terms.", error.Message);
    }

    [TestMethod]
    [DataRow(50, true)]
    [DataRow(51, false)]
    public void CompileQueryGroupDepthBudgetIsFinite(int depth, bool accepted)
    {
        string input = new string('(', depth) + "alpha" + new string(')', depth);
        if (accepted)
        {
            var query = BoardPolicy.CompileQuery(input);
            Assert.HasCount(1, query.Parameters);
            Assert.AreEqual("alpha", query.Parameters[0].Value);
            return;
        }

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            BoardPolicy.CompileQuery(input)
        );
        Assert.AreEqual("invalid_arguments", error.Code);
        Assert.Contains("balanced parentheses", error.Message);
    }

    [TestMethod]
    [DataRow(1L)]
    [DataRow(10000L)]
    [DataRow(300000000L)]
    public void WorkAdmissionExactDeadlineCancelsPreviouslyObtainedToken(long durationTicks)
    {
        ManualScenarioClock clock = new();
        using WorkAdmission admission = new(
            clock,
            clock.GetUtcNow().AddTicks(durationTicks),
            CancellationToken.None
        );
        CancellationToken token = admission.Token;
        bool notified = false;
        using CancellationTokenRegistration registration = token.Register(() => notified = true);

        clock.Advance(TimeSpan.FromTicks(durationTicks - 1));
        Assert.IsFalse(admission.IsClosed);
        Assert.IsFalse(token.IsCancellationRequested);
        admission.ThrowIfClosed();
        NativeWorker.CheckRenewalAdmission(admission);

        clock.Advance(TimeSpan.FromTicks(1));
        // Observe the captured token before reading IsClosed: the manual timer must deliver.
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsTrue(notified);
        Assert.IsTrue(admission.IsClosed);
        OperationCanceledException error = Assert.ThrowsExactly<OperationCanceledException>(
            admission.ThrowIfClosed
        );
        Assert.AreEqual(token, error.CancellationToken);
        var renewal = Assert.ThrowsExactly<NativeWorker.RenewalAdmissionCanceledException>(() =>
            NativeWorker.CheckRenewalAdmission(admission)
        );
        Assert.AreEqual(token, renewal.CancellationToken);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void WorkAdmissionAlreadyExpiredRejectsWithoutAdvancingClock(long offsetTicks)
    {
        ManualScenarioClock clock = new();
        using WorkAdmission admission = new(
            clock,
            clock.GetUtcNow().AddTicks(offsetTicks),
            CancellationToken.None
        );

        Assert.IsTrue(admission.IsClosed);
        Assert.IsTrue(admission.Token.IsCancellationRequested);
        var error = Assert.ThrowsExactly<NativeWorker.RenewalAdmissionCanceledException>(() =>
            NativeWorker.CheckRenewalAdmission(admission)
        );
        Assert.AreEqual(admission.Token, error.CancellationToken);
    }

    [TestMethod]
    public async Task WorkAdmissionExplicitCloseRejectsRenewalBeforeDeadline()
    {
        ManualScenarioClock clock = new();
        using WorkAdmission admission = new(
            clock,
            clock.GetUtcNow().AddHours(1),
            CancellationToken.None
        );
        CancellationToken token = admission.Token;

        await admission.CloseAsync();
        await admission.CloseAsync();

        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsTrue(admission.IsClosed);
        var error = Assert.ThrowsExactly<NativeWorker.RenewalAdmissionCanceledException>(() =>
            NativeWorker.CheckRenewalAdmission(admission)
        );
        Assert.AreEqual(token, error.CancellationToken);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), clock.GetUtcNow());
    }

    [TestMethod]
    public void WorkAdmissionCallerCancellationAndDisposedSiblingDoNotLoseShortDeadline()
    {
        ManualScenarioClock clock = new();
        using CancellationTokenSource caller = new();
        using WorkAdmission first = new(clock, clock.GetUtcNow().AddTicks(2), caller.Token);
        WorkAdmission disposed = new(clock, clock.GetUtcNow().AddTicks(1), CancellationToken.None);
        CancellationToken retiredToken = disposed.Token;
        disposed.Dispose();
        using WorkAdmission next = new(
            clock,
            clock.GetUtcNow().AddTicks(3),
            CancellationToken.None
        );
        CancellationToken nextToken = next.Token;
        using CancellationTokenRegistration registration = first.Token.Register(() =>
            clock.Advance(TimeSpan.FromTicks(1))
        );

        caller.Cancel();

        Assert.IsTrue(first.IsClosed);
        Assert.IsFalse(retiredToken.IsCancellationRequested);
        Assert.IsFalse(nextToken.IsCancellationRequested);
        clock.Advance(TimeSpan.FromTicks(2));
        Assert.IsTrue(nextToken.IsCancellationRequested);
        Assert.IsTrue(next.IsClosed);
        var error = Assert.ThrowsExactly<NativeWorker.RenewalAdmissionCanceledException>(() =>
            NativeWorker.CheckRenewalAdmission(first)
        );
        Assert.AreEqual(first.Token, error.CancellationToken);
    }

    [TestMethod]
    public void AdmitPatchEvidenceOnlyAfterTruncationBoundaryIsNotAdmittedAsVisibleProof()
    {
        string content = new string('x', 300) + " | evidence=passed";

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            BoardPolicy.Admit("PATCH_SUMMARY", content)
        );

        Assert.AreEqual("invalid_arguments", error.Code);
        Assert.AreEqual("Board admission rejected placeholder patch evidence.", error.Message);
    }
}
