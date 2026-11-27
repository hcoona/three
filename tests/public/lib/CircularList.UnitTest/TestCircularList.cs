// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CsCheck;
using IO.Github.Hcoona.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IO.Github.Hcoona.Collections.UnitTest;

[TestClass]
public sealed class TestCircularList
{
    private const string DeterministicSeed = "circularlist1951";
    private static readonly int[] ExpectedSingleEviction = [10];
    private static readonly int[] ExpectedFirstSegment = [3, 4];
    private static readonly int[] ExpectedSecondSegment = [2, 5];
    private static readonly int[] ExpectedAfterClearAdd = [40];
    private static readonly int[] ExpectedReentrantEvictions = [1, 2];
    private static readonly int[] ExpectedReentrantItems = [3, 4, 99];
    private static readonly int[] ExpectedPostThrowItems = [20, 30];
    private static readonly int[] ExpectedPreservedItems = [10, 20];

    private enum CommandKind
    {
        Add,
        SetAt,
        Clear,
    }

    private readonly record struct RingCommand(CommandKind Kind, int Argument, int Value);

    [TestMethod]
    public void StatefulRingOperations_RefineBoundedFifoReferenceModel()
    {
        var genCommand = Gen.Frequency(
            (6, Gen.Int[-50, 50].Select(static v => new RingCommand(CommandKind.Add, 0, v))),
            (2, Gen.Select(Gen.Int[0, 32], Gen.Int[-50, 50])
                .Select(static (idx, v) => new RingCommand(CommandKind.SetAt, idx, v))),
            (1, Gen.Const(new RingCommand(CommandKind.Clear, 0, 0))));

        var genScenario = Gen.Select(Gen.Int[1, 12], genCommand.Array[0, 64]);

        genScenario.Sample(
            static scenario =>
            {
                (int capacity, RingCommand[] commands) = scenario;
                var ring = new CircularList<int>(capacity);
                var model = new List<int>(capacity);
                var actualEvictions = new List<int>();
                var expectedEvictions = new List<int>();

                ring.OnOverflow += (sender, evicted) =>
                {
                    Assert.AreSame(ring, sender);
                    actualEvictions.Add(evicted);
                };

                Assert.AreEqual(capacity, ring.Capacity);
                AssertRefinesModel(ring, model, actualEvictions, expectedEvictions);

                foreach (RingCommand cmd in commands)
                {
                    switch (cmd.Kind)
                    {
                        case CommandKind.Add:
                            {
                                CircularList<int>.Enumerator staleStruct = ring.GetEnumerator();
                                using IEnumerator<int> staleBoxed =
                                    ((IEnumerable<int>)ring).GetEnumerator();

                                ring.Add(cmd.Value);
                                if (model.Count == capacity)
                                {
                                    expectedEvictions.Add(model[0]);
                                    model.RemoveAt(0);
                                }

                                model.Add(cmd.Value);

                                AssertInvalidatedEnumerator(staleStruct, staleBoxed);
                                break;
                            }

                        case CommandKind.SetAt:
                            {
                                if (model.Count == 0)
                                {
                                    Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                                        () => ring[0] = cmd.Value);
                                }
                                else
                                {
                                    int targetIndex = cmd.Argument % model.Count;
                                    CircularList<int>.Enumerator staleStruct = ring.GetEnumerator();
                                    using IEnumerator<int> staleBoxed =
                                        ((IEnumerable<int>)ring).GetEnumerator();

                                    ring[targetIndex] = cmd.Value;
                                    model[targetIndex] = cmd.Value;

                                    AssertInvalidatedEnumerator(staleStruct, staleBoxed);
                                }

                                break;
                            }

                        case CommandKind.Clear:
                            {
                                CircularList<int>.Enumerator staleStruct = ring.GetEnumerator();
                                using IEnumerator<int> staleBoxed =
                                    ((IEnumerable<int>)ring).GetEnumerator();

                                ring.Clear();
                                model.Clear();

                                AssertInvalidatedEnumerator(staleStruct, staleBoxed);
                                break;
                            }
                    }

                    Assert.AreEqual(capacity, ring.Capacity);
                    AssertRefinesModel(ring, model, actualEvictions, expectedEvictions);
                }
            },
            seed: DeterministicSeed,
            iter: 400,
            threads: 1);
    }

    [TestMethod]
    public void AddAndIndexOf_AcrossCapacityOneAndWrappedDuplicateSegments_PreserveLogicalOrder()
    {
        var single = new CircularList<int>(1);
        var evictedFromSingle = new List<int>();
        single.OnOverflow += (_, item) => evictedFromSingle.Add(item);

        single.Add(10);
        Assert.HasCount(1, single);
        Assert.AreEqual(10, single[0]);

        single.Add(20);
        Assert.HasCount(1, single);
        Assert.AreEqual(20, single[0]);
        Assert.AreSequenceEqual(ExpectedSingleEviction, evictedFromSingle);

        var ring = new CircularList<int>(4);
        ring.Add(1);
        ring.Add(2);
        ring.Add(3);
        ring.Add(4);
        ring.Add(2);
        ring.Add(5);

        // Logical sequence is [3, 4, 2, 5] split across physical tail [3, 4] and head [2, 5].
        (ReadOnlySpan<int> first, ReadOnlySpan<int> second) = ring.GetSegments();
        Assert.AreSequenceEqual(ExpectedFirstSegment, first.ToArray());
        Assert.AreSequenceEqual(ExpectedSecondSegment, second.ToArray());
        Assert.AreEqual(2, ring.IndexOf(2));
        Assert.AreEqual(3, ring.IndexOf(5));
        Assert.AreEqual(-1, ring.IndexOf(1));

        // Put duplicate 4 in both physical segments: [4, 2, 5, 4] -> IndexOf(4) == 0.
        ring.Add(4);
        Assert.AreEqual(0, ring.IndexOf(4));
    }

    [TestMethod]
    public void Add_WhenOnOverflowMutatesOrThrows_CommitsTriggerStateBeforeCallback()
    {
        // Scenario 1: Reentrant Clear() inside OnOverflow observes committed state and wins.
        var clearOnOverflow = new CircularList<int>(2);
        int observedCountInsideCallback = -1;
        int observedEvicted = -1;
        clearOnOverflow.OnOverflow += (_, evicted) =>
        {
            observedEvicted = evicted;
            observedCountInsideCallback = clearOnOverflow.Count;
            clearOnOverflow.Clear();
        };

        clearOnOverflow.Add(10);
        clearOnOverflow.Add(20);
        clearOnOverflow.Add(30);

        Assert.AreEqual(10, observedEvicted);
        Assert.AreEqual(2, observedCountInsideCallback);
        Assert.IsEmpty(clearOnOverflow);

        clearOnOverflow.Add(40);
        Assert.AreSequenceEqual(ExpectedAfterClearAdd, clearOnOverflow.ToArray());

        // Scenario 2: Reentrant Add() inside OnOverflow appends after the triggering item.
        var addOnOverflow = new CircularList<int>(3);
        var reentrantEvictions = new List<int>();
        bool reentered = false;
        addOnOverflow.OnOverflow += (_, evicted) =>
        {
            reentrantEvictions.Add(evicted);
            if (!reentered)
            {
                reentered = true;
                addOnOverflow.Add(99);
            }
        };

        addOnOverflow.Add(1);
        addOnOverflow.Add(2);
        addOnOverflow.Add(3);
        addOnOverflow.Add(4);

        Assert.AreSequenceEqual(ExpectedReentrantEvictions, reentrantEvictions);
        Assert.AreSequenceEqual(ExpectedReentrantItems, addOnOverflow.ToArray());

        // Scenario 3: Exception thrown by OnOverflow leaves the already-committed ring intact.
        var throwOnOverflow = new CircularList<int>(2);
        throwOnOverflow.OnOverflow += static (_, _) =>
            throw new InvalidOperationException("Subscriber failure.");

        throwOnOverflow.Add(10);
        throwOnOverflow.Add(20);
        Assert.ThrowsExactly<InvalidOperationException>(() => throwOnOverflow.Add(30));
        Assert.AreSequenceEqual(ExpectedPostThrowItems, throwOnOverflow.ToArray());
    }

    [TestMethod]
    public void Clear_ReferenceTypeElements_ReleasesBackingArrayReferencesForGc()
    {
        var list = new CircularList<object>(3);
        WeakReference[] refs = PopulateWrappedReferencesAndClear(list);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        foreach (WeakReference weakRef in refs)
        {
            Assert.IsFalse(weakRef.IsAlive);
        }

        GC.KeepAlive(list);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void Constructor_NonPositiveCapacity_ThrowsArgumentOutOfRangeException(
        int invalidCapacity)
    {
        var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _ = new CircularList<int>(invalidCapacity));
        Assert.AreEqual("capacity", ex.ParamName);
    }

    [TestMethod]
    public void PublicGuardsAndUnsupportedMutations_ThrowExpectedExceptionsWithoutCorruptingState()
    {
        var list = new CircularList<int>(3);

        // Empty list CopyTo at exact end of 0-length array succeeds.
        list.CopyTo(Array.Empty<int>(), 0);

        list.Add(10);
        list.Add(20);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = list[-1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = list[2]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => list[-1] = 0);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => list[2] = 0);

        Assert.ThrowsExactly<ArgumentNullException>(() => list.CopyTo(null!, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => list.CopyTo(new int[4], -1));
        Assert.ThrowsExactly<ArgumentException>(() => list.CopyTo(new int[4], 3));
        Assert.ThrowsExactly<ArgumentException>(() => list.CopyTo(new int[1].AsSpan()));

        CircularList<int>.Enumerator activeEnumerator = list.GetEnumerator();
        Assert.IsTrue(activeEnumerator.MoveNext());
        Assert.AreEqual(10, activeEnumerator.Current);

        Assert.ThrowsExactly<NotSupportedException>(() => list.Insert(0, 99));
        Assert.ThrowsExactly<NotSupportedException>(() => _ = list.Remove(10));
        Assert.ThrowsExactly<NotSupportedException>(() => list.RemoveAt(0));

        // Unsupported operations do not mutate the list or invalidate open enumerators.
        Assert.IsTrue(activeEnumerator.MoveNext());
        Assert.AreEqual(20, activeEnumerator.Current);
        Assert.IsFalse(activeEnumerator.MoveNext());
        Assert.AreSequenceEqual(ExpectedPreservedItems, list.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] PopulateWrappedReferencesAndClear(CircularList<object> list)
    {
        var o1 = new object();
        var o2 = new object();
        var o3 = new object();
        var o4 = new object();

        list.Add(o1);
        list.Add(o2);
        list.Add(o3);
        list.Add(o4);
        list.Clear();

        return
        [
            new WeakReference(o1),
            new WeakReference(o2),
            new WeakReference(o3),
            new WeakReference(o4),
        ];
    }

    private static void AssertInvalidatedEnumerator(
        CircularList<int>.Enumerator staleStruct,
        IEnumerator<int> staleBoxed)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = staleStruct.MoveNext());
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = staleBoxed.MoveNext());
        Assert.ThrowsExactly<InvalidOperationException>(staleBoxed.Reset);
    }

    private static void AssertRefinesModel(
        CircularList<int> actual,
        List<int> model,
        List<int> actualEvictions,
        List<int> expectedEvictions)
    {
        Assert.AreEqual(model.Count, actual.Count);
        Assert.IsFalse(actual.IsReadOnly);
        Assert.AreSequenceEqual(expectedEvictions, actualEvictions);

        for (int i = 0; i < model.Count; i++)
        {
            Assert.AreEqual(model[i], actual[i]);
            int value = model[i];
            Assert.IsTrue(actual.Contains(value));
            Assert.AreEqual(model.IndexOf(value), actual.IndexOf(value));
        }

        Assert.IsFalse(actual.Contains(-999_999));
        Assert.AreEqual(-1, actual.IndexOf(-999_999));

        CircularList<int>.Segments segments = actual.GetSegments();
        Assert.AreEqual(model.Count, segments.Count);
        Assert.AreEqual(model.Count == 0, segments.IsEmpty);
        if (model.Count == 0)
        {
            Assert.IsTrue(segments.First.IsEmpty);
            Assert.IsTrue(segments.Second.IsEmpty);
        }

        int[] fromSegments = [.. segments.First, .. segments.Second];
        Assert.AreSequenceEqual(model, fromSegments);

        int[] arrayCopy = new int[model.Count + 3];
        arrayCopy[0] = 777;
        arrayCopy[1] = 888;
        arrayCopy[^1] = 999;
        actual.CopyTo(arrayCopy, 2);
        Assert.AreEqual(777, arrayCopy[0]);
        Assert.AreEqual(888, arrayCopy[1]);
        Assert.AreEqual(999, arrayCopy[^1]);
        Assert.AreSequenceEqual(model, arrayCopy.AsSpan(2, model.Count).ToArray());

        int[] spanCopy = new int[model.Count];
        actual.CopyTo(spanCopy.AsSpan());
        Assert.AreSequenceEqual(model, spanCopy);

        ReadOnlySpan<int> asSpan = actual.AsSpan();
        Assert.AreSequenceEqual(model, asSpan.ToArray());
        if (model.Count > 0)
        {
            int originalZero = actual[0];
            actual[0] = originalZero ^ 0x5A5A;
            if (segments.Second.IsEmpty)
            {
                // Contiguous storage returns a zero-allocation ReadOnlySpan over internal array.
                Assert.AreEqual(originalZero ^ 0x5A5A, asSpan[0]);
            }
            else
            {
                // Wrapped storage returns an allocated contiguous snapshot copy.
                Assert.AreEqual(originalZero, asSpan[0]);
            }

            actual[0] = originalZero;
        }

        var fromStructEnumerator = new List<int>(model.Count);
        foreach (int item in actual)
        {
            fromStructEnumerator.Add(item);
        }

        Assert.AreSequenceEqual(model, fromStructEnumerator);

        using IEnumerator<int> boxed = ((IEnumerable<int>)actual).GetEnumerator();
        var nonGeneric = (IEnumerator)boxed;
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = nonGeneric.Current);

        var firstPass = new List<int>(model.Count);
        while (nonGeneric.MoveNext())
        {
            firstPass.Add((int)nonGeneric.Current!);
        }

        Assert.AreSequenceEqual(model, firstPass);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = nonGeneric.Current);

        nonGeneric.Reset();
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = nonGeneric.Current);
        var secondPass = new List<int>(model.Count);
        while (boxed.MoveNext())
        {
            secondPass.Add(boxed.Current);
        }

        Assert.AreSequenceEqual(model, secondPass);
    }
}
