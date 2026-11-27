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
using System.Threading;
using CsCheck;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Memoization.Tests
{
    /// <summary>
    /// Exhaustive finite-state formal specifications (<see cref="Spec"/>), specification
    /// mutation proofs (<see cref="Check.Faults{S}"/>), and exhaustive transition-by-transition
    /// C# implementation conformance (<c>Check.ConformExhaustive</c>).
    /// </summary>
    [TestClass]
    public sealed class MemoizationSpecTests
    {
        public enum SlotState : byte
        {
            Absent = 0,
            Rev0 = 1,
            Rev1 = 2,
        }

        public enum OuterState : byte
        {
            Absent = 0,
            FromH = 1,
            FromFallback = 2,
        }

        public enum StepKind : byte
        {
            Init = 0,
            CallF0 = 1,
            CallG0 = 2,
            CallH0 = 3,
            CallOuter = 4,
            FlipRev = 5,
            ToggleFailF0 = 6,
            ToggleFailH = 7,
            FireTokenH = 8,
            FireChildToken = 9,
            FireFallbackToken = 10,
        }

        public enum InvokedWho : byte
        {
            None = 0,
            F = 1,
            G = 2,
            H = 3,
        }

        public readonly record struct LifecycleState(
            SlotState F0,
            SlotState G0,
            SlotState H0,
            OuterState Outer,
            byte Rev,
            bool FailF0,
            bool FailH,
            bool TokenHFired,
            bool ChildTokenFired,
            bool FallbackTokenFired,
            StepKind LastStep,
            int LastReturn,
            bool LastThrew,
            InvokedWho LastInvoked)
        {
            public static LifecycleState Initial => new(
                SlotState.Absent,
                SlotState.Absent,
                SlotState.Absent,
                OuterState.Absent,
                Rev: 0,
                FailF0: false,
                FailH: false,
                TokenHFired: false,
                ChildTokenFired: false,
                FallbackTokenFired: false,
                StepKind.Init,
                LastReturn: 0,
                LastThrew: false,
                InvokedWho.None);

            public byte ChildRev => ChildTokenFired ? (byte)1 : (byte)0;

            public static int ValueF(byte rev, int key) => 100 + (10 * rev) + key;

            public static int ValueG(byte rev, int key) => 200 + (10 * rev) + key;

            public static int ValueH(byte childRev, int key) => 300 + (10 * childRev) + key;

            public const int FallbackValue = 999;

            public static byte SlotRevision(SlotState slot)
                => slot == SlotState.Rev1 ? (byte)1 : (byte)0;

            public static SlotState ToSlot(byte rev)
                => rev == 0 ? SlotState.Rev0 : SlotState.Rev1;

            private LifecycleState ResetStep(StepKind kind) => this with
            {
                LastStep = kind,
                LastReturn = 0,
                LastThrew = false,
                LastInvoked = InvokedWho.None,
            };

            public LifecycleState StepCallF0()
            {
                var next = ResetStep(StepKind.CallF0);
                if (F0 != SlotState.Absent)
                {
                    return next with { LastReturn = ValueF(SlotRevision(F0), 0) };
                }

                if (FailF0)
                {
                    return next with { LastThrew = true, LastInvoked = InvokedWho.F };
                }

                return next with
                {
                    F0 = ToSlot(Rev),
                    LastReturn = ValueF(Rev, 0),
                    LastInvoked = InvokedWho.F,
                };
            }

            public LifecycleState StepCallG0()
            {
                var next = ResetStep(StepKind.CallG0);
                if (G0 != SlotState.Absent)
                {
                    return next with { LastReturn = ValueG(SlotRevision(G0), 0) };
                }

                return next with
                {
                    G0 = ToSlot(Rev),
                    LastReturn = ValueG(Rev, 0),
                    LastInvoked = InvokedWho.G,
                };
            }

            public LifecycleState StepCallH0()
            {
                var next = ResetStep(StepKind.CallH0);
                if (H0 != SlotState.Absent)
                {
                    return next with { LastReturn = ValueH(SlotRevision(H0), 0) };
                }

                if (FailH)
                {
                    return next with { LastThrew = true, LastInvoked = InvokedWho.H };
                }

                // If TokenH has already fired on the reusable options instance, the newly
                // created entry expires upon commit while still returning the computed value.
                var committedSlot = TokenHFired ? SlotState.Absent : ToSlot(ChildRev);
                return next with
                {
                    H0 = committedSlot,
                    LastReturn = ValueH(ChildRev, 0),
                    LastInvoked = InvokedWho.H,
                };
            }

            public LifecycleState StepCallOuter()
            {
                var next = ResetStep(StepKind.CallOuter);
                if (Outer != OuterState.Absent)
                {
                    var cachedOuterVal = Outer == OuterState.FromFallback
                        ? FallbackValue
                        : ValueH(H0 != SlotState.Absent ? SlotRevision(H0) : ChildRev, 0);
                    return next with { LastReturn = cachedOuterVal };
                }

                if (H0 != SlotState.Absent)
                {
                    // Outer hits cached H0; MemoryCache propagates H0's tokens to Outer on hit too.
                    return next with
                    {
                        Outer = OuterState.FromH,
                        LastReturn = ValueH(SlotRevision(H0), 0),
                    };
                }

                if (FailH)
                {
                    // H0 throws without setting Value: Outer catches and reads fallback entry.
                    var committedOuter = FallbackTokenFired
                        ? OuterState.Absent
                        : OuterState.FromFallback;
                    return next with
                    {
                        Outer = committedOuter,
                        LastReturn = FallbackValue,
                        LastInvoked = InvokedWho.H,
                    };
                }

                var hSlot = TokenHFired ? SlotState.Absent : ToSlot(ChildRev);
                var outerState = TokenHFired ? OuterState.Absent : OuterState.FromH;
                return next with
                {
                    H0 = hSlot,
                    Outer = outerState,
                    LastReturn = ValueH(ChildRev, 0),
                    LastInvoked = InvokedWho.H,
                };
            }

            public LifecycleState StepFlipRev()
                => ResetStep(StepKind.FlipRev) with { Rev = (byte)(1 - Rev) };

            public LifecycleState StepToggleFailF0()
                => ResetStep(StepKind.ToggleFailF0) with { FailF0 = !FailF0 };

            public LifecycleState StepToggleFailH()
                => ResetStep(StepKind.ToggleFailH) with { FailH = !FailH };

            public LifecycleState StepFireTokenH() => ResetStep(StepKind.FireTokenH) with
            {
                TokenHFired = true,
                H0 = SlotState.Absent,
                Outer = Outer == OuterState.FromH ? OuterState.Absent : Outer,
            };

            public LifecycleState StepFireChildToken() => ResetStep(StepKind.FireChildToken) with
            {
                ChildTokenFired = true,
                H0 = SlotState.Absent,
                Outer = Outer == OuterState.FromH ? OuterState.Absent : Outer,
            };

            public LifecycleState StepFireFallbackToken() =>
                ResetStep(StepKind.FireFallbackToken) with
                {
                    FallbackTokenFired = true,
                    Outer = Outer == OuterState.FromFallback ? OuterState.Absent : Outer,
                };

            public override string ToString()
            {
                var res = LastThrew ? "throw" : LastReturn.ToString();
                return $"F0={F0} G0={G0} H0={H0} Outer={Outer} rev={Rev} fail=({FailF0},{FailH}) " +
                    $"tok=({TokenHFired},{ChildTokenFired},{FallbackTokenFired}) " +
                    $"-> {LastStep}:{res} inv={LastInvoked}";
            }
        }

        private static Spec<LifecycleState> CreateLifecycleSpec()
            => Spec.From(LifecycleState.Initial)
                .Print(static s => s.ToString())
                .Action("CallF0", static s => s.StepCallF0())
                .Action("CallG0", static s => s.StepCallG0())
                .Action("CallH0", static s => s.StepCallH0())
                .Action(
                    "CallOuter",
                    static s => !s.TokenHFired && !s.ChildTokenFired,
                    static s => s.StepCallOuter())
                .Action("FlipRev", static s => s.StepFlipRev())
                .Action("ToggleFailF0", static s => s.StepToggleFailF0())
                .Action("ToggleFailH", static s => s.StepToggleFailH())
                .Action("FireTokenH", static s => !s.TokenHFired, static s => s.StepFireTokenH())
                .Action(
                    "FireChildToken",
                    static s => !s.ChildTokenFired,
                    static s => s.StepFireChildToken())
                .Action(
                    "FireFallbackToken",
                    static s => !s.FallbackTokenFired,
                    static s => s.StepFireFallbackToken())

                .Rule(
                    "DELEGATE-ISOLATION",
                    "Distinct delegates sharing one cache never cross-read or overwrite slots.",
                    when: static (_, a) =>
                        a.LastStep is StepKind.CallF0 or StepKind.CallG0 && !a.LastThrew,
                    then: static (b, a) => a.LastStep switch
                    {
                        StepKind.CallF0 => a.G0 == b.G0 && a.LastReturn is 100 or 110,
                        StepKind.CallG0 => a.F0 == b.F0 && a.LastReturn is 200 or 210,
                        _ => true,
                    })
                .Rule(
                    "CACHE-HIT-NO-RECOMPUTE",
                    "A cache hit returns the cached value without invoking the source function.",
                    when: static (b, a) =>
                        (a.LastStep == StepKind.CallF0 && b.F0 != SlotState.Absent) ||
                        (a.LastStep == StepKind.CallG0 && b.G0 != SlotState.Absent) ||
                        (a.LastStep == StepKind.CallH0 && b.H0 != SlotState.Absent),
                    then: static (b, a) =>
                        !a.LastThrew &&
                        a.LastInvoked == InvokedWho.None &&
                        a.LastStep switch
                        {
                            StepKind.CallF0 => a.LastReturn ==
                                LifecycleState.ValueF(LifecycleState.SlotRevision(b.F0), 0),
                            StepKind.CallG0 => a.LastReturn ==
                                LifecycleState.ValueG(LifecycleState.SlotRevision(b.G0), 0),
                            StepKind.CallH0 => a.LastReturn ==
                                LifecycleState.ValueH(LifecycleState.SlotRevision(b.H0), 0),
                            _ => false,
                        })
                .Rule(
                    "CACHE-MISS-POPULATES-ENTRY",
                    "A non-throwing miss invokes the source function once and populates its slot.",
                    when: static (b, a) =>
                        a.LastStep == StepKind.CallF0 && b.F0 == SlotState.Absent && !b.FailF0,
                    then: static (b, a) =>
                        !a.LastThrew &&
                        a.LastInvoked == InvokedWho.F &&
                        a.F0 == LifecycleState.ToSlot(b.Rev) &&
                        a.LastReturn == LifecycleState.ValueF(b.Rev, 0))
                .Rule(
                    "EXCEPTION-NO-POLLUTE",
                    "A throwing miss leaves its own slot absent and preserves unrelated entries.",
                    when: static (b, a) =>
                        (a.LastStep == StepKind.CallF0 && b.F0 == SlotState.Absent && b.FailF0) ||
                        (a.LastStep == StepKind.CallH0 && b.H0 == SlotState.Absent && b.FailH),
                    then: static (b, a) =>
                        a.LastThrew &&
                        (a.LastStep != StepKind.CallF0 ||
                            (a.F0 == SlotState.Absent && a.G0 == b.G0)) &&
                        (a.LastStep != StepKind.CallH0 ||
                            (a.H0 == SlotState.Absent && a.F0 == b.F0)))
                .Rule(
                    "DIRECT-OPTIONS-EXPIRATION",
                    "Signaling a change token on options invalidates only dependent entries.",
                    on: "FireTokenH",
                    then: static (b, a) =>
                        a.H0 == SlotState.Absent &&
                        (b.Outer != OuterState.FromH || a.Outer == OuterState.Absent) &&
                        a.F0 == b.F0 &&
                        a.G0 == b.G0)
                .Rule(
                    "LINKED-CHILD-EXPIRATION",
                    "Signaling a child entry token invalidates the memoized entry with options.",
                    on: "FireChildToken",
                    then: static (b, a) =>
                        a.H0 == SlotState.Absent &&
                        (b.Outer != OuterState.FromH || a.Outer == OuterState.Absent))
                .Rule(
                    "OUTER-FALLBACK-SURVIVES-THROW",
                    "Throwing inside an outer GetOrCreate preserves fallback expiration tracking.",
                    on: "FireFallbackToken",
                    when: static (b, _) => b.Outer == OuterState.FromFallback,
                    then: static (_, a) => a.Outer == OuterState.Absent)

                .Reachable(
                    "REACH-STALE-HIT-AFTER-REV-FLIP",
                    "A cached result is reused after the underlying revision changes.",
                    static s =>
                        s.LastStep == StepKind.CallF0 &&
                        s.Rev == 1 &&
                        !s.LastThrew &&
                        s.LastReturn == 100 &&
                        s.LastInvoked == InvokedWho.None)
                .Reachable(
                    "REACH-RETRY-SUCCEEDS-AFTER-THROW",
                    "A key that previously threw succeeds and caches once failure clears.",
                    static s => s.F0 != SlotState.Absent && s.FailF0)
                .Reachable(
                    "REACH-CHILD-TOKEN-REFRESHES-H",
                    "After child token expiration, H0 recomputes against the updated child value.",
                    static s =>
                        s.LastStep == StepKind.CallH0 &&
                        s.ChildTokenFired &&
                        !s.LastThrew &&
                        s.LastReturn == 310)
                .Reachable(
                    "REACH-OUTER-FALLBACK-EXPIRES",
                    "Outer fallback entry after H0 threw expires when fallback token fires.",
                    static s =>
                        s.LastStep == StepKind.FireFallbackToken &&
                        s.FallbackTokenFired &&
                        s.Outer == OuterState.Absent &&
                        s.FailH)

                .Fault(
                    "unscoped cache key collides across delegates",
                    static (b, a) =>
                        a.LastStep == StepKind.CallG0 &&
                        b.G0 == SlotState.Absent &&
                        b.F0 != SlotState.Absent,
                    static (b, a) => a with
                    {
                        LastReturn = LifecycleState.ValueF(LifecycleState.SlotRevision(b.F0), 0),
                        LastInvoked = InvokedWho.None,
                    })
                .Fault(
                    "cache hit recomputes underlying function",
                    static (b, a) => a.LastStep == StepKind.CallF0 && b.F0 != SlotState.Absent,
                    static (b, a) => a with
                    {
                        LastReturn = LifecycleState.ValueF(b.Rev, 0),
                        LastInvoked = InvokedWho.F,
                    })
                .Fault(
                    "successful miss fails to populate cache",
                    static (b, a) =>
                        a.LastStep == StepKind.CallF0 && b.F0 == SlotState.Absent && !b.FailF0,
                    static (_, a) => a with { F0 = SlotState.Absent })
                .Fault(
                    "throwing computation pollutes cache",
                    static (b, a) =>
                        a.LastStep == StepKind.CallF0 && b.F0 == SlotState.Absent && b.FailF0,
                    static (_, a) => a with { F0 = SlotState.Rev0 })
                .Fault(
                    "options expiration token ignored",
                    static (b, a) =>
                        a.LastStep == StepKind.FireTokenH && b.H0 != SlotState.Absent,
                    static (b, a) => a with { H0 = b.H0 })
                .Fault(
                    "SetOptions overwrites linked child expiration",
                    static (b, a) =>
                        a.LastStep == StepKind.FireChildToken && b.H0 != SlotState.Absent,
                    static (b, a) => a with { H0 = b.H0 })
                .Fault(
                    "throwing inner entry breaks outer linked tracking",
                    static (b, a) =>
                        a.LastStep == StepKind.FireFallbackToken &&
                        b.Outer == OuterState.FromFallback,
                    static (_, a) => a with { Outer = OuterState.FromFallback });

        private sealed class RealLifecycleSut : IDisposable
        {
            private readonly MemoryCache _cache =
                new(new MemoryCacheOptions { TrackLinkedCacheEntries = true });
            private readonly CancellationTokenSource _tokenHCts = new();
            private readonly CancellationTokenSource _childCts0 = new();
            private readonly CancellationTokenSource _fallbackCts = new();
            private readonly Func<int, int> _f;
            private readonly Func<int, int> _g;
            private readonly Func<int, int> _h;

            private byte _rev;
            private bool _failF0;
            private bool _failH;
            private bool _childFired;

            public RealLifecycleSut()
            {
                _f = Memoization.Create<int, int>(
                    key =>
                    {
                        LastInvoked = InvokedWho.F;
                        if (key == 0 && _failF0)
                        {
                            throw new InvalidOperationException("F0 failed.");
                        }

                        return LifecycleState.ValueF(_rev, key);
                    },
                    _cache);

                _g = Memoization.Create<int, int>(
                    key =>
                    {
                        LastInvoked = InvokedWho.G;
                        return LifecycleState.ValueG(_rev, key);
                    },
                    _cache);

                var optionsH = new MemoryCacheEntryOptions()
                    .AddExpirationToken(new CancellationChangeToken(_tokenHCts.Token));
                _h = Memoization.Create<int, int>(
                    key =>
                    {
                        LastInvoked = InvokedWho.H;
                        if (_failH)
                        {
                            throw new InvalidOperationException("H0 failed.");
                        }

                        var childVal = _cache.GetOrCreate(
                            "child",
                            entry =>
                            {
                                if (!_childFired)
                                {
                                    var token = new CancellationChangeToken(_childCts0.Token);
                                    entry.AddExpirationToken(token);
                                    return (byte)0;
                                }

                                return (byte)1;
                            });
                        return LifecycleState.ValueH(childVal, key);
                    },
                    _cache,
                    optionsH);
            }

            public InvokedWho LastInvoked { get; private set; }

            public string? Apply(Transition<LifecycleState> step)
            {
                LastInvoked = InvokedWho.None;
                var expected = step.After;

                switch (step.Action)
                {
                    case "CallF0":
                        return VerifyCall(() => _f(0), expected);
                    case "CallG0":
                        return VerifyCall(() => _g(0), expected);
                    case "CallH0":
                        return VerifyCall(() => _h(0), expected);
                    case "CallOuter":
                        return VerifyCall(
                            () => _cache.GetOrCreate(
                                "outer",
                                _ =>
                                {
                                    try
                                    {
                                        return _h(0);
                                    }
                                    catch (InvalidOperationException)
                                    {
                                        return _cache.GetOrCreate(
                                            "fallback",
                                            fallbackEntry =>
                                            {
                                                var token = new CancellationChangeToken(
                                                    _fallbackCts.Token);
                                                fallbackEntry.AddExpirationToken(token);
                                                return LifecycleState.FallbackValue;
                                            });
                                    }
                                }),
                            expected);
                    case "FlipRev":
                        _rev = (byte)(1 - _rev);
                        return null;
                    case "ToggleFailF0":
                        _failF0 = !_failF0;
                        return null;
                    case "ToggleFailH":
                        _failH = !_failH;
                        return null;
                    case "FireTokenH":
                        _tokenHCts.Cancel();
                        return CheckOuterMatches(expected);
                    case "FireChildToken":
                        _childFired = true;
                        _childCts0.Cancel();
                        return CheckOuterMatches(expected);
                    case "FireFallbackToken":
                        _fallbackCts.Cancel();
                        return CheckOuterMatches(expected);
                    default:
                        return $"Unexpected action {step.Action}.";
                }
            }

            private string? VerifyCall(Func<int> action, LifecycleState expected)
            {
                int actualReturn = 0;
                bool actualThrew = false;
                try
                {
                    actualReturn = action();
                }
                catch (InvalidOperationException)
                {
                    actualThrew = true;
                }

                if (actualThrew != expected.LastThrew)
                {
                    return $"Expected LastThrew={expected.LastThrew}, actual={actualThrew}.";
                }

                if (!actualThrew && actualReturn != expected.LastReturn)
                {
                    return $"Expected return={expected.LastReturn}, actual={actualReturn}.";
                }

                if (LastInvoked != expected.LastInvoked)
                {
                    return $"Expected LastInvoked={expected.LastInvoked}, actual={LastInvoked}.";
                }

                return CheckOuterMatches(expected);
            }

            private string? CheckOuterMatches(LifecycleState expected)
            {
                bool actualOuterPresent = _cache.TryGetValue("outer", out _);
                bool expectedOuterPresent = expected.Outer != OuterState.Absent;
                return actualOuterPresent == expectedOuterPresent
                    ? null
                    : $"Outer expected={expectedOuterPresent}, actual={actualOuterPresent}.";
            }

            public void Dispose()
            {
                _cache.Dispose();
                _tokenHCts.Dispose();
                _childCts0.Dispose();
                _fallbackCts.Dispose();
            }
        }

        [TestMethod]
        public void LifecycleSpec_ExhaustiveProof_ClosesWithoutDeadlocksOrVacuousRules()
        {
            var report = CreateLifecycleSpec().Exhaustive();

            Assert.IsTrue(report.Closed, report.ToString());
            Assert.AreEqual(0, report.DeadlockStates, report.ToString());
            Assert.IsEmpty(report.NeverTriggered, report.ToString());
            Assert.IsEmpty(report.NeverFired, report.ToString());
            Assert.IsGreaterThan(1000, report.States);
        }

        [TestMethod]
        public void LifecycleSpec_MutationFaults_AreAllCaughtAndExerciseEverySafetyRule()
        {
            var faults = CreateLifecycleSpec().Faults();

            Assert.IsEmpty(faults.Uncaught, faults.ToString());
            Assert.IsEmpty(faults.Inconclusive, faults.ToString());
            Assert.IsEmpty(faults.Unexercised, faults.ToString());

            Assert.AreEqual(
                "DELEGATE-ISOLATION",
                faults.CaughtBy("unscoped cache key collides across delegates"));
            Assert.AreEqual(
                "CACHE-HIT-NO-RECOMPUTE",
                faults.CaughtBy("cache hit recomputes underlying function"));
            Assert.AreEqual(
                "CACHE-MISS-POPULATES-ENTRY",
                faults.CaughtBy("successful miss fails to populate cache"));
            Assert.AreEqual(
                "EXCEPTION-NO-POLLUTE",
                faults.CaughtBy("throwing computation pollutes cache"));
            Assert.AreEqual(
                "DIRECT-OPTIONS-EXPIRATION",
                faults.CaughtBy("options expiration token ignored"));
            Assert.AreEqual(
                "LINKED-CHILD-EXPIRATION",
                faults.CaughtBy("SetOptions overwrites linked child expiration"));
            Assert.AreEqual(
                "OUTER-FALLBACK-SURVIVES-THROW",
                faults.CaughtBy("throwing inner entry breaks outer linked tracking"));
        }

        [TestMethod]
        public void LifecycleSpec_ConformExhaustive_RealMemoryCacheConformsOnEveryTransition()
        {
            var spec = CreateLifecycleSpec();
            var instances = new ConcurrentBag<RealLifecycleSut>();

            try
            {
                var report = spec.ConformExhaustive(
                    create: () =>
                    {
                        var sut = new RealLifecycleSut();
                        instances.Add(sut);
                        return sut;
                    },
                    apply: static (sut, step) => sut.Apply(step));

                Assert.IsTrue(report.Closed, report.ToString());
                Assert.AreEqual(report.States, report.ConformedStates, report.ToString());
                Assert.AreEqual(report.Transitions, report.ConformedTransitions, report.ToString());
            }
            finally
            {
                foreach (var sut in instances)
                {
                    sut.Dispose();
                }
            }
        }

        public enum WorkerPhase : byte
        {
            Idle = 0,
            Lookup = 1,
            Compute = 2,
            Publish = 3,
            Returned = 4,
            Failed = 5,
        }

        public readonly record struct WorkerCall(WorkerPhase Phase, byte Wrapper, byte Result)
        {
            public static WorkerCall Idle => new(WorkerPhase.Idle, 0, 0);
        }

        public readonly record struct InterleavingState(
            byte CacheF,
            byte CacheG,
            WorkerCall W1,
            WorkerCall W2)
        {
            public static InterleavingState Initial => new(0, 0, WorkerCall.Idle, WorkerCall.Idle);

            public static byte ExpectedValue(byte wrapper)
                => wrapper == 0 ? (byte)11 : (byte)22;

            public WorkerCall Of(int worker) => worker == 0 ? W1 : W2;

            public InterleavingState WithWorker(int worker, WorkerCall call)
                => worker == 0 ? this with { W1 = call } : this with { W2 = call };

            public byte ReadSlot(byte wrapper) => wrapper == 0 ? CacheF : CacheG;

            public InterleavingState WriteSlot(byte wrapper, byte val)
                => wrapper == 0 ? this with { CacheF = val } : this with { CacheG = val };

            public override string ToString()
                => $"cache=({CacheF},{CacheG}) " +
                    $"w1={W1.Phase}:d{W1.Wrapper}=>{W1.Result} " +
                    $"w2={W2.Phase}:d{W2.Wrapper}=>{W2.Result}";
        }

        private static readonly (int Worker, byte Wrapper)[] StartChoices =
        [
            (0, 0),
            (0, 1),
            (1, 0),
            (1, 1),
        ];

        private static readonly int[] Workers = [0, 1];
        private static readonly byte[] Wrappers = [0, 1];

        private static Spec<InterleavingState> CreateInterleavingSpec()
            => Spec.From(InterleavingState.Initial)
                .Print(static s => s.ToString())
                .Action(
                    "Start",
                    StartChoices,
                    static (s, c) => s.Of(c.Worker).Phase is
                        WorkerPhase.Idle or WorkerPhase.Returned or WorkerPhase.Failed,
                    static (s, c) => s.WithWorker(
                        c.Worker,
                        new WorkerCall(WorkerPhase.Lookup, c.Wrapper, 0)))
                .Action(
                    "Lookup",
                    Workers,
                    static (s, w) => s.Of(w).Phase == WorkerPhase.Lookup,
                    static (s, w) =>
                    {
                        var call = s.Of(w);
                        var cached = s.ReadSlot(call.Wrapper);
                        return cached == 0
                            ? s.WithWorker(w, call with { Phase = WorkerPhase.Compute })
                            : s.WithWorker(
                                w,
                                new WorkerCall(WorkerPhase.Returned, call.Wrapper, cached));
                    })
                .Action(
                    "ComputeOk",
                    Workers,
                    static (s, w) => s.Of(w).Phase == WorkerPhase.Compute,
                    static (s, w) =>
                    {
                        var call = s.Of(w);
                        return s.WithWorker(w, call with { Phase = WorkerPhase.Publish });
                    })
                .Action(
                    "ComputeFail",
                    Workers,
                    static (s, w) => s.Of(w).Phase == WorkerPhase.Compute,
                    static (s, w) =>
                    {
                        var call = s.Of(w);
                        return s.WithWorker(w, new WorkerCall(WorkerPhase.Failed, call.Wrapper, 0));
                    })
                .Action(
                    "Publish",
                    Workers,
                    static (s, w) => s.Of(w).Phase == WorkerPhase.Publish,
                    static (s, w) =>
                    {
                        var call = s.Of(w);
                        var val = InterleavingState.ExpectedValue(call.Wrapper);
                        return s
                            .WriteSlot(call.Wrapper, val)
                            .WithWorker(w, new WorkerCall(WorkerPhase.Returned, call.Wrapper, val));
                    })
                .Action(
                    "Evict",
                    Wrappers,
                    static (s, wrapper) => s.ReadSlot(wrapper) != 0,
                    static (s, wrapper) => s.WriteSlot(wrapper, 0))

                .Invariant(
                    "NO-FOREIGN-RESULT",
                    "Under arbitrary interleavings and evictions, no caller sees foreign results.",
                    static s =>
                        (s.W1.Phase != WorkerPhase.Returned ||
                            s.W1.Result == InterleavingState.ExpectedValue(s.W1.Wrapper)) &&
                        (s.W2.Phase != WorkerPhase.Returned ||
                            s.W2.Result == InterleavingState.ExpectedValue(s.W2.Wrapper)))
                .Rule(
                    "FAILURE-NEVER-PUBLISHES",
                    "A failing computation transitions to Failed without mutating any cache slot.",
                    on: "ComputeFail",
                    then: static (b, a) => a.CacheF == b.CacheF && a.CacheG == b.CacheG)
                .Rule(
                    "LOOKUP-HIT-SHORT-CIRCUITS",
                    "A lookup observing a populated slot returns it without entering Compute.",
                    on: "Lookup",
                    when: static (b, a) =>
                        (b.W1.Phase == WorkerPhase.Lookup &&
                            a.W1.Phase != WorkerPhase.Lookup &&
                            b.ReadSlot(b.W1.Wrapper) != 0) ||
                        (b.W2.Phase == WorkerPhase.Lookup &&
                            a.W2.Phase != WorkerPhase.Lookup &&
                            b.ReadSlot(b.W2.Wrapper) != 0),
                    then: static (b, a) =>
                        (b.W1.Phase != WorkerPhase.Lookup ||
                            a.W1.Phase == WorkerPhase.Lookup ||
                            b.ReadSlot(b.W1.Wrapper) == 0 ||
                            (a.W1.Phase == WorkerPhase.Returned &&
                                a.W1.Result == b.ReadSlot(b.W1.Wrapper))) &&
                        (b.W2.Phase != WorkerPhase.Lookup ||
                            a.W2.Phase == WorkerPhase.Lookup ||
                            b.ReadSlot(b.W2.Wrapper) == 0 ||
                            (a.W2.Phase == WorkerPhase.Returned &&
                                a.W2.Result == b.ReadSlot(b.W2.Wrapper))))
                .Reachable(
                    "CONCURRENT-DUPLICATE-COMPUTE-PERMITTED",
                    "Two concurrent cold misses on one key may both compute (non-single-flight).",
                    static s =>
                        s.W1.Phase == WorkerPhase.Compute &&
                        s.W2.Phase == WorkerPhase.Compute &&
                        s.W1.Wrapper == s.W2.Wrapper)

                .Fault(
                    "shared unscoped slot returns foreign delegate value",
                    static (b, a) =>
                        b.W1.Phase == WorkerPhase.Lookup &&
                        a.W1.Phase == WorkerPhase.Compute &&
                        b.W1.Wrapper == 1 &&
                        b.CacheF != 0,
                    static (b, a) =>
                        a with { W1 = new WorkerCall(WorkerPhase.Returned, 1, b.CacheF) })
                .Fault(
                    "failing computation publishes corrupt entry",
                    static (b, a) =>
                        b.W1.Phase == WorkerPhase.Compute && a.W1.Phase == WorkerPhase.Failed,
                    static (_, a) => a with { CacheF = 99 })
                .Fault(
                    "lookup ignores populated slot and recomputes",
                    static (b, a) =>
                        b.W1.Phase == WorkerPhase.Lookup &&
                        a.W1.Phase == WorkerPhase.Returned &&
                        b.ReadSlot(b.W1.Wrapper) != 0,
                    static (b, a) => a with { W1 = b.W1 with { Phase = WorkerPhase.Compute } });

        [TestMethod]
        public void InterleavingSpec_ProofAndFaults_VerifyConcurrentSafetyAndNonSingleFlight()
        {
            var spec = CreateInterleavingSpec();
            var report = spec.Exhaustive();

            Assert.IsTrue(report.Closed, report.ToString());
            Assert.AreEqual(0, report.DeadlockStates, report.ToString());
            Assert.IsEmpty(report.NeverTriggered, report.ToString());
            Assert.IsEmpty(report.NeverFired, report.ToString());

            var faults = spec.Faults();
            Assert.IsEmpty(faults.Uncaught, faults.ToString());
            Assert.IsEmpty(faults.Inconclusive, faults.ToString());
            Assert.IsEmpty(faults.Unexercised, faults.ToString());
        }
    }
}
