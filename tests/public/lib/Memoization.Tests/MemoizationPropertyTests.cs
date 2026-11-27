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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using CsCheck;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Memoization.Tests
{
    /// <summary>
    /// Stateful model-based refinement tests (<c>Check.SampleModelBased</c>) and multi-threaded
    /// linearizability tests (<c>Check.SampleParallel</c>) for <see cref="Memoization"/>.
    /// </summary>
    [TestClass]
    public sealed class MemoizationPropertyTests
    {
        private const string ModelBasedSeed = "memoization01";
        private const string ParallelSeed = "memoization02";

        private sealed class FakeClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; set; } =
                new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public void AdvanceSeconds(int seconds) => UtcNow = UtcNow.AddSeconds(seconds);
        }

        private readonly record struct CallOutcome(
            bool Threw,
            string? TextResult,
            int? NullableIntResult);

        private sealed class ActualStatefulHarness : IDisposable
        {
            private readonly FakeClock _clock = new();
            private readonly MemoryCache _cache;
            private readonly CancellationTokenSource[] _cts = [new(), new()];
            private readonly Func<string?, int?, string?> _primaryStrings;
            private readonly Func<string?, int?, string?> _secondaryStrings;
            private readonly Func<string?, int?, int?> _timedNullableInts;

            private int _revision;
            private readonly HashSet<(int DelegateId, string? S, int? N)> _failing = [];

            public ActualStatefulHarness()
            {
                _cache = new MemoryCache(new MemoryCacheOptions
                {
                    Clock = _clock,
                    SizeLimit = 256,
                    TrackLinkedCacheEntries = true,
                });

                var opts0 = new MemoryCacheEntryOptions { Size = 1 }
                    .AddExpirationToken(new CancellationChangeToken(_cts[0].Token));
                var opts1 = new MemoryCacheEntryOptions { Size = 1 }
                    .AddExpirationToken(new CancellationChangeToken(_cts[1].Token));
                var timedOpts = new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10),
                };

                _primaryStrings = Memoization.Create<string?, int?, string?>(
                    (s, n) =>
                    {
                        Calls[0]++;
                        if (_failing.Contains((0, s, n)))
                        {
                            throw new InvalidOperationException("Simulated primary failure.");
                        }

                        return ComputePrimary(s, n, _revision);
                    },
                    _cache,
                    opts0);

                _secondaryStrings = Memoization.Create<string?, int?, string?>(
                    (s, n) =>
                    {
                        Calls[1]++;
                        if (_failing.Contains((1, s, n)))
                        {
                            throw new InvalidOperationException("Simulated secondary failure.");
                        }

                        return ComputeSecondary(s, n, _revision);
                    },
                    _cache,
                    opts1);

                _timedNullableInts = Memoization.Create<string?, int?, int?>(
                    (s, n) =>
                    {
                        Calls[2]++;
                        if (_failing.Contains((2, s, n)))
                        {
                            throw new InvalidOperationException("Simulated timed failure.");
                        }

                        return ComputeNullableInt(s, n, _revision);
                    },
                    _cache,
                    timedOpts);
            }

            public int[] Calls { get; } = new int[3];

            public CallOutcome LastOutcome { get; private set; }

            public static string? ComputePrimary(string? s, int? n, int rev)
            {
                if (s is null && n is null)
                {
                    return null;
                }

                var nText = n?.ToString(CultureInfo.InvariantCulture) ?? "<null>";
                return $"P:{s ?? "<null>"}:{nText}@r{rev}";
            }

            public static string? ComputeSecondary(string? s, int? n, int rev)
            {
                if (s == "null-out")
                {
                    return null;
                }

                var nText = n?.ToString(CultureInfo.InvariantCulture) ?? "<null>";
                return $"S:{s ?? "<null>"}:{nText}@r{rev}";
            }

            public static int? ComputeNullableInt(string? s, int? n, int rev)
                => n is null ? null : (s?.Length ?? -10) + (n.Value * 100) + rev;

            public void Invoke(
                int delegateId,
                string? rawKey,
                bool cloneStringInstance,
                int? numKey)
            {
                // Allocate a distinct string instance when requested to verify structural value
                // equality rather than reference equality in ValueTuple cache keys.
                string? s = cloneStringInstance && rawKey is not null
                    ? new string(rawKey.ToCharArray())
                    : rawKey;

                try
                {
                    switch (delegateId)
                    {
                        case 0:
                            LastOutcome = new CallOutcome(
                                false,
                                _primaryStrings(s, numKey),
                                null);
                            break;
                        case 1:
                            LastOutcome = new CallOutcome(
                                false,
                                _secondaryStrings(s, numKey),
                                null);
                            break;
                        default:
                            LastOutcome = new CallOutcome(
                                false,
                                null,
                                _timedNullableInts(s, numKey));
                            break;
                    }
                }
                catch (InvalidOperationException)
                {
                    LastOutcome = new CallOutcome(true, null, null);
                }
            }

            public void BumpRevision() => _revision++;

            public void ToggleFailure(int delegateId, string? s, int? n)
            {
                var item = (delegateId, s, n);
                if (!_failing.Remove(item))
                {
                    _failing.Add(item);
                }
            }

            public void CancelToken(int tokenIndex) => _cts[tokenIndex].Cancel();

            public void AdvanceClock(int seconds) => _clock.AdvanceSeconds(seconds);

            public void Dispose()
            {
                _cache.Dispose();
                _cts[0].Dispose();
                _cts[1].Dispose();
            }

            public override string ToString()
                => $"Actual(calls=[{string.Join(',', Calls)}], outcome={LastOutcome})";
        }

        private sealed class ReferenceStatefulModel
        {
            private readonly record struct TimedEntry(int? Value, int ExpiresAtSeconds);

            private readonly Dictionary<(string? S, int? N), string?> _slot0 = [];
            private readonly Dictionary<(string? S, int? N), string?> _slot1 = [];
            private readonly Dictionary<(string? S, int? N), TimedEntry> _slot2 = [];
            private readonly HashSet<(int DelegateId, string? S, int? N)> _failing = [];
            private readonly bool[] _tokenCancelled = new bool[2];
            private int _nowSeconds;
            private int _revision;

            public int[] Calls { get; } = new int[3];

            public CallOutcome LastOutcome { get; private set; }

            public void Invoke(int delegateId, string? s, int? n)
            {
                var key = (s, n);
                switch (delegateId)
                {
                    case 0:
                        if (!_tokenCancelled[0] && _slot0.TryGetValue(key, out var cached0))
                        {
                            LastOutcome = new CallOutcome(false, cached0, null);
                            return;
                        }

                        Calls[0]++;
                        if (_failing.Contains((0, s, n)))
                        {
                            LastOutcome = new CallOutcome(true, null, null);
                            return;
                        }

                        var val0 = ActualStatefulHarness.ComputePrimary(s, n, _revision);
                        if (!_tokenCancelled[0])
                        {
                            _slot0[key] = val0;
                        }

                        LastOutcome = new CallOutcome(false, val0, null);
                        return;

                    case 1:
                        if (!_tokenCancelled[1] && _slot1.TryGetValue(key, out var cached1))
                        {
                            LastOutcome = new CallOutcome(false, cached1, null);
                            return;
                        }

                        Calls[1]++;
                        if (_failing.Contains((1, s, n)))
                        {
                            LastOutcome = new CallOutcome(true, null, null);
                            return;
                        }

                        var val1 = ActualStatefulHarness.ComputeSecondary(s, n, _revision);
                        if (!_tokenCancelled[1])
                        {
                            _slot1[key] = val1;
                        }

                        LastOutcome = new CallOutcome(false, val1, null);
                        return;

                    default:
                        if (_slot2.TryGetValue(key, out var cached2))
                        {
                            if (_nowSeconds < cached2.ExpiresAtSeconds)
                            {
                                LastOutcome = new CallOutcome(false, null, cached2.Value);
                                return;
                            }

                            _slot2.Remove(key);
                        }

                        Calls[2]++;
                        if (_failing.Contains((2, s, n)))
                        {
                            LastOutcome = new CallOutcome(true, null, null);
                            return;
                        }

                        var val2 = ActualStatefulHarness.ComputeNullableInt(s, n, _revision);
                        _slot2[key] = new TimedEntry(val2, _nowSeconds + 10);
                        LastOutcome = new CallOutcome(false, null, val2);
                        return;
                }
            }

            public void BumpRevision() => _revision++;

            public void ToggleFailure(int delegateId, string? s, int? n)
            {
                var item = (delegateId, s, n);
                if (!_failing.Remove(item))
                {
                    _failing.Add(item);
                }
            }

            public void CancelToken(int tokenIndex)
            {
                _tokenCancelled[tokenIndex] = true;
                if (tokenIndex == 0)
                {
                    _slot0.Clear();
                }
                else
                {
                    _slot1.Clear();
                }
            }

            public void AdvanceClock(int seconds) => _nowSeconds += seconds;

            public override string ToString()
                => $"Model(calls=[{string.Join(',', Calls)}], outcome={LastOutcome})";
        }

        [TestMethod]
        public void ModelBased_NullableMultiArityTokensAndDiscreteExpiration_RefineReferenceModel()
        {
            var genStringKey = Gen.OneOf<string?>(null, string.Empty, "alpha", "null-out");
            var genIntKey = Gen.OneOf<int?>(null, 0, 1, -1);
            var genDelegate = Gen.Int[0, 2];

            var disposables = new List<ActualStatefulHarness>();
            try
            {
                Gen.Const(() =>
                {
                    var actual = new ActualStatefulHarness();
                    disposables.Add(actual);
                    return (actual, new ReferenceStatefulModel());
                })
                .SampleModelBased(
                    Gen.Select(genDelegate, genStringKey, Gen.Bool, genIntKey)
                        .Operation<ActualStatefulHarness, ReferenceStatefulModel>(
                            (actual, args) =>
                                actual.Invoke(args.Item1, args.Item2, args.Item3, args.Item4),
                            (model, args) =>
                                model.Invoke(args.Item1, args.Item2, args.Item4)),
                    Gen.Operation<ActualStatefulHarness, ReferenceStatefulModel>(
                        static actual => actual.BumpRevision(),
                        static model => model.BumpRevision()),
                    Gen.Select(genDelegate, genStringKey, genIntKey)
                        .Operation<ActualStatefulHarness, ReferenceStatefulModel>(
                            (actual, args) =>
                                actual.ToggleFailure(args.Item1, args.Item2, args.Item3),
                            (model, args) =>
                                model.ToggleFailure(args.Item1, args.Item2, args.Item3)),
                    Gen.Int[0, 1]
                        .Operation<ActualStatefulHarness, ReferenceStatefulModel>(
                            (actual, tokenIdx) => actual.CancelToken(tokenIdx),
                            (model, tokenIdx) => model.CancelToken(tokenIdx)),
                    Gen.Int[1, 6]
                        .Operation<ActualStatefulHarness, ReferenceStatefulModel>(
                            (actual, sec) => actual.AdvanceClock(sec),
                            (model, sec) => model.AdvanceClock(sec)),
                    equal: static (actual, model) =>
                        actual.LastOutcome == model.LastOutcome &&
                        actual.Calls.SequenceEqual(model.Calls),
                    seed: ModelBasedSeed,
                    iter: 250,
                    threads: 1,
                    printActual: static actual => actual.ToString(),
                    printModel: static model => model.ToString());
            }
            finally
            {
                foreach (var d in disposables)
                {
                    d.Dispose();
                }
            }
        }

        private sealed class ConcurrentHarness : IDisposable
        {
            private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 32 });
            private readonly Func<string?, int, string?>[] _delegates;
            private readonly ConcurrentDictionary<(int Id, string? S, int N), CallOutcome> _seen =
                new();
            private int _failureCorruptionCount;

            public ConcurrentHarness()
            {
                var options = new MemoryCacheEntryOptions { Size = 1 };
                _delegates =
                [
                    Memoization.Create<string?, int, string?>(
                        (s, n) => ComputeSpec(0, s, n),
                        _cache,
                        options),
                    Memoization.Create<string?, int, string?>(
                        (s, n) => ComputeSpec(1, s, n),
                        _cache,
                        options),
                ];
            }

            public static string? ComputeSpec(int delegateId, string? s, int n)
            {
                if (n < 0)
                {
                    throw new InvalidOperationException("Negative input rejected.");
                }

                if (s is null && n == 0)
                {
                    return null;
                }

                return $"D{delegateId}:{s ?? "<null>"}:{n.ToString(CultureInfo.InvariantCulture)}";
            }

            public void Invoke(int delegateId, string? s, int n)
            {
                CallOutcome outcome;
                try
                {
                    var value = _delegates[delegateId](s, n);
                    outcome = new CallOutcome(false, value, null);
                }
                catch (InvalidOperationException)
                {
                    outcome = new CallOutcome(true, null, null);
                }

                var expected = ExpectedOutcome(delegateId, s, n);
                if (outcome != expected)
                {
                    Interlocked.Increment(ref _failureCorruptionCount);
                }

                _seen[(delegateId, s, n)] = outcome;
            }

            public void Compact() => _cache.Compact(0.5);

            public static CallOutcome ExpectedOutcome(int delegateId, string? s, int n)
                => n < 0
                    ? new CallOutcome(true, null, null)
                    : new CallOutcome(false, ComputeSpec(delegateId, s, n), null);

            public bool MatchesSequentialModel(SequentialConcurrentModel model)
            {
                if (Volatile.Read(ref _failureCorruptionCount) != 0)
                {
                    return false;
                }

                if (_seen.Count != model.Observed.Count)
                {
                    return false;
                }

                foreach (var kvp in model.Observed)
                {
                    if (!_seen.TryGetValue(kvp.Key, out var actualOutcome) ||
                        actualOutcome != kvp.Value)
                    {
                        return false;
                    }
                }

                return true;
            }

            public void Dispose() => _cache.Dispose();

            public override string ToString()
                => $"ConcurrentActual(corruptions={_failureCorruptionCount}, count={_seen.Count})";
        }

        private sealed class SequentialConcurrentModel
        {
            public Dictionary<(int Id, string? S, int N), CallOutcome> Observed { get; } = [];

            public void Invoke(int delegateId, string? s, int n)
                => Observed[(delegateId, s, n)] =
                    ConcurrentHarness.ExpectedOutcome(delegateId, s, n);

            public static void Compact()
            {
            }

            public override string ToString() => $"ConcurrentModel(count={Observed.Count})";
        }

        [TestMethod]
        public void SampleParallel_SharedCacheAndCompaction_LinearizeWithoutContamination()
        {
            var genKeyString = Gen.OneOf<string?>(null, "k0", "k1");
            var genKeyInt = Gen.Int[-1, 2];
            var disposables = new ConcurrentBag<ConcurrentHarness>();

            try
            {
                Gen.Const(() =>
                {
                    var actual = new ConcurrentHarness();
                    disposables.Add(actual);
                    return (actual, new SequentialConcurrentModel());
                })
                .SampleParallel(
                    Gen.Select(Gen.Int[0, 1], genKeyString, genKeyInt)
                        .Operation<ConcurrentHarness, SequentialConcurrentModel>(
                            static args =>
                                $"Invoke(d{args.Item1}, {args.Item2 ?? "null"}, {args.Item3})",
                            static (actual, args) =>
                                actual.Invoke(args.Item1, args.Item2, args.Item3),
                            static (model, args) =>
                                model.Invoke(args.Item1, args.Item2, args.Item3)),
                    Gen.Operation<ConcurrentHarness, SequentialConcurrentModel>(
                        "Compact()",
                        static actual => actual.Compact(),
                        static _ => SequentialConcurrentModel.Compact()),
                    equal: static (actual, model) => actual.MatchesSequentialModel(model),
                    seed: ParallelSeed,
                    maxSequentialOperations: 6,
                    maxParallelOperations: 5,
                    iter: 120,
                    threads: 1,
                    printActual: static actual => actual.ToString(),
                    printModel: static model => model.ToString());
            }
            finally
            {
                foreach (var d in disposables)
                {
                    d.Dispose();
                }
            }
        }
    }
}
