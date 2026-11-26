using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class SeqTests
{
    [TestMethod]
    public void Equals_IndependentSequences_UsesOrderedElementEquality()
    {
        Seq<int> first = [1, 2, 3];
        Seq<int> same = Seq.Create<int>([1, 2, 3]);
        Seq<int> reordered = [3, 2, 1];
        Seq<int> shorter = [1, 2];

        Assert.AreEqual(first, same);
        Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(first, reordered);
        Assert.AreNotEqual(first, shorter);
        Assert.IsTrue(first == same);
        Assert.IsTrue(first != reordered);
    }

    [TestMethod]
    public void Equals_NestedAndNullableSequences_UsesValueEqualityInRecords()
    {
        var first = new AstNode([[1, 2], []], ["name", null]);
        var same = new AstNode([[1, 2], default], ["name", null]);
        var changed = new AstNode([[1, 2], []], [null, "name"]);

        Assert.AreEqual(first, same);
        Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(first, changed);
    }

    [TestMethod]
    public void Default_IsEmptyAndSupportsAllReadPaths()
    {
        Seq<int> value = default;

        Assert.AreEqual(Seq<int>.Empty, value);
        Assert.AreEqual(Seq<int>.Empty.GetHashCode(), value.GetHashCode());
        Assert.IsEmpty(value);
        Assert.IsTrue(value.AsSpan().IsEmpty);
        Assert.AreEqual(Seq<int>.Empty, value.Slice(0, 0));
        Assert.AreEqual(Seq<string>.Empty, value.Select(static _ => "value"));
        Assert.ThrowsExactly<IndexOutOfRangeException>(() => _ = value[0]);
    }

    [TestMethod]
    public void ListPattern_CollectionWithSpread_BindsHeadAndSlice()
    {
        Seq<int> rest = [3, 4];
        Seq<int> sequence = [1, 2, .. rest];

        if (sequence is not [var first, .. var tail])
        {
            Assert.Fail("Expected a sequence with at least one element.");
            return;
        }

        Seq<int> expectedTail = [2, 3, 4];
        Seq<int> expectedProjection = [4, 6, 8];
        Assert.AreEqual(1, first);
        Assert.AreEqual(expectedTail, tail);
        Assert.AreEqual(expectedProjection, tail.Select(static item => item * 2));
    }

    private sealed record AstNode(Seq<Seq<int>> Rows, Seq<string?> Names);
}
