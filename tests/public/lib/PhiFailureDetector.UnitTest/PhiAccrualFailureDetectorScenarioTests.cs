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
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PhiFailureDetector.UnitTest;

/// <summary>
/// End-to-end distributed heartbeat scenario tests for <see cref="PhiAccrualFailureDetector"/>.
/// </summary>
[TestClass]
public sealed class PhiAccrualFailureDetectorScenarioTests(TestContext testContext)
{
    [TestMethod]
    public void IdlePeer_BeforeFirstHeartbeat_RemainsAvailableWithZeroSuspicion()
    {
        var clock = new FakeTimeProvider();
        var detector = new PhiAccrualFailureDetector(
            new PhiAccrualFailureDetectorOptions { Threshold = 8.0 },
            clock);

        var initial = detector.Evaluate();
        Assert.IsFalse(initial.IsMonitoring);
        Assert.IsNull(initial.Elapsed);
        Assert.AreEqual(0.0, initial.Phi);
        Assert.IsTrue(initial.IsAvailable);
        Assert.AreEqual(0.0, detector.GetPhi());
        Assert.IsTrue(detector.IsAvailable());
        Assert.AreEqual(0, initial.Statistics.SampleCount);
        Assert.AreEqual(TimeSpan.Zero, initial.Statistics.Mean);
        Assert.AreEqual(TimeSpan.Zero, initial.Statistics.StandardDeviation);

        clock.Advance(TimeSpan.FromHours(6));

        var afterHours = detector.Evaluate();
        Assert.IsFalse(afterHours.IsMonitoring);
        Assert.IsNull(afterHours.Elapsed);
        Assert.AreEqual(0.0, afterHours.Phi);
        Assert.IsTrue(afterHours.IsAvailable);
        Assert.AreEqual(0.0, detector.GetPhi());
        Assert.IsTrue(detector.IsAvailable());
    }

    [TestMethod]
    public void SingleHeartbeatBootstrap_PeerCrashesImmediately_AccruesSuspicionWithoutNaN()
    {
        var baseOptions = new PhiAccrualFailureDetectorOptions
        {
            Threshold = 8.0,
            InitialHeartbeatEstimate = TimeSpan.FromSeconds(1),
            MinStandardDeviation = TimeSpan.FromMilliseconds(100),
        };

        foreach (var distribution in new[] { PhiDistribution.Normal, PhiDistribution.Exponential })
        {
            var clock = new FakeTimeProvider();
            var detector = new PhiAccrualFailureDetector(
                baseOptions with { Distribution = distribution },
                clock);

            detector.RecordHeartbeat();

            var atArrival = detector.Evaluate();
            Assert.IsTrue(atArrival.IsMonitoring);
            Assert.AreEqual(TimeSpan.Zero, atArrival.Elapsed);
            Assert.AreEqual(0, atArrival.Statistics.SampleCount);
            Assert.IsFalse(double.IsNaN(atArrival.Phi));
            Assert.IsGreaterThanOrEqualTo(0.0, atArrival.Phi);
            Assert.IsTrue(atArrival.IsAvailable);

            clock.Advance(TimeSpan.FromMilliseconds(500));
            var early = detector.Evaluate();
            Assert.AreEqual(TimeSpan.FromMilliseconds(500), early.Elapsed);
            Assert.IsGreaterThanOrEqualTo(atArrival.Phi, early.Phi);
            Assert.IsTrue(early.IsAvailable);

            clock.Advance(TimeSpan.FromSeconds(2));
            var mid = detector.Evaluate();
            Assert.IsGreaterThan(early.Phi, mid.Phi);

            clock.Advance(TimeSpan.FromSeconds(20));
            var crashed = detector.Evaluate();
            Assert.IsFalse(double.IsNaN(crashed.Phi));
            Assert.IsGreaterThan(8.0, crashed.Phi);
            Assert.IsFalse(crashed.IsAvailable);
            Assert.IsFalse(detector.IsAvailable());
        }
    }

    [TestMethod]
    public void SteadyStateCluster_HigherJitterCushionsSuspicionWhileCrashRecovers()
    {
        var options = new PhiAccrualFailureDetectorOptions
        {
            Threshold = 8.0,
            MinStandardDeviation = TimeSpan.FromMilliseconds(10),
            Distribution = PhiDistribution.Normal,
        };

        var clock = new FakeTimeProvider();
        var lowJitterPeer = new PhiAccrualFailureDetector(options, clock);
        lowJitterPeer.RecordHeartbeat();

        // Feed 40 intervals with identical 100 ms mean, but +/- 5 ms vs +/- 45 ms jitter.
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(95));
            lowJitterPeer.RecordHeartbeat();
            clock.Advance(TimeSpan.FromMilliseconds(105));
            lowJitterPeer.RecordHeartbeat();
        }

        var highClock = new FakeTimeProvider();
        var highJitterPeer = new PhiAccrualFailureDetector(options, highClock);
        highJitterPeer.RecordHeartbeat();
        for (var i = 0; i < 20; i++)
        {
            highClock.Advance(TimeSpan.FromMilliseconds(55));
            highJitterPeer.RecordHeartbeat();
            highClock.Advance(TimeSpan.FromMilliseconds(145));
            highJitterPeer.RecordHeartbeat();
        }

        var lowStats = lowJitterPeer.Evaluate().Statistics;
        var highStats = highJitterPeer.Evaluate().Statistics;
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), lowStats.Mean);
        Assert.AreEqual(TimeSpan.FromMilliseconds(100), highStats.Mean);
        Assert.IsLessThan(highStats.StandardDeviation, lowStats.StandardDeviation);

        // At 180 ms silence, low-jitter peer is suspected while high-jitter peer stays healthy.
        clock.Advance(TimeSpan.FromMilliseconds(180));
        highClock.Advance(TimeSpan.FromMilliseconds(180));

        Assert.IsFalse(lowJitterPeer.IsAvailable());
        Assert.IsTrue(highJitterPeer.IsAvailable());

        // Sustained silence eventually marks the high-jitter peer unavailable as well.
        highClock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsFalse(highJitterPeer.IsAvailable());

        // When a delayed heartbeat arrives, suspicion drops immediately and availability recovers.
        lowJitterPeer.RecordHeartbeat();
        Assert.IsTrue(lowJitterPeer.IsAvailable());
    }

    [TestMethod]
    public void AcceptableHeartbeatPause_ToleratesTransientGcStallWithoutFalsePositive()
    {
        var clock = new FakeTimeProvider();
        var strictOptions = new PhiAccrualFailureDetectorOptions
        {
            Threshold = 8.0,
            AcceptableHeartbeatPause = TimeSpan.Zero,
            MinStandardDeviation = TimeSpan.FromMilliseconds(50),
        };
        var strictDetector = new PhiAccrualFailureDetector(strictOptions, clock);
        var pauseTolerantDetector = new PhiAccrualFailureDetector(
            strictOptions with { AcceptableHeartbeatPause = TimeSpan.FromSeconds(3) },
            clock);

        strictDetector.RecordHeartbeat();
        pauseTolerantDetector.RecordHeartbeat();

        for (var i = 0; i < 30; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            strictDetector.RecordHeartbeat();
            pauseTolerantDetector.RecordHeartbeat();
        }

        // Simulate a 2.5-second full-GC / transient packet pause.
        clock.Advance(TimeSpan.FromMilliseconds(2500));

        Assert.IsFalse(strictDetector.IsAvailable());
        Assert.IsTrue(pauseTolerantDetector.IsAvailable());

        // Sustained crash well past the 3-second pause cushion still triggers detection.
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.IsFalse(pauseTolerantDetector.IsAvailable());
    }

    [TestMethod]
    public void SlidingWindowAndReset_AdaptsToCadenceChangeAndClearsAcrossPeerReboot()
    {
        var clock = new FakeTimeProvider();
        var detector = new PhiAccrualFailureDetector(
            new PhiAccrualFailureDetectorOptions
            {
                Threshold = 8.0,
                MaxSampleSize = 16,
                MinStandardDeviation = TimeSpan.FromMilliseconds(20),
            },
            clock);

        detector.RecordHeartbeat();
        for (var i = 0; i < 16; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50));
            detector.RecordHeartbeat();
        }

        var fastCadence = detector.Evaluate().Statistics;
        Assert.AreEqual(16, fastCadence.SampleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(50), fastCadence.Mean);

        // Peer reconfigures its heartbeat interval to 500 ms; window evicts old 50 ms samples.
        for (var i = 0; i < 16; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            detector.RecordHeartbeat();
        }

        var slowCadence = detector.Evaluate().Statistics;
        Assert.AreEqual(16, slowCadence.SampleCount);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), slowCadence.Mean);
        Assert.AreEqual(TimeSpan.Zero, slowCadence.StandardDeviation);

        // 400 ms elapsed would fail under 50 ms cadence, but stays healthy under 500 ms cadence.
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.IsTrue(detector.IsAvailable());

        // Peer reboots and starts a fresh incarnation epoch.
        detector.Reset();
        var resetSnapshot = detector.Evaluate();
        Assert.IsFalse(resetSnapshot.IsMonitoring);
        Assert.IsNull(resetSnapshot.Elapsed);
        Assert.AreEqual(0, resetSnapshot.Statistics.SampleCount);
        Assert.AreEqual(0.0, resetSnapshot.Phi);
        Assert.IsTrue(resetSnapshot.IsAvailable);
        Assert.AreEqual(0.0, detector.GetPhi());
        Assert.IsTrue(detector.IsAvailable());
    }

    [TestMethod]
    public void ExponentialGossipAndMultiDaySilence_PreservesBase10ScaleAndFailsClosed()
    {
        var clock = new FakeTimeProvider();
        var detector = new PhiAccrualFailureDetector(
            new PhiAccrualFailureDetectorOptions
            {
                Threshold = 8.0,
                Distribution = PhiDistribution.Exponential,
            },
            clock);

        detector.RecordHeartbeat();

        // Use 10 s gossip intervals (exceeding the ~3.037 s 64-bit tick-squaring limit on 1 GHz).
        for (var i = 0; i < 25; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            detector.RecordHeartbeat();
        }

        Assert.AreEqual(TimeSpan.FromSeconds(10), detector.Evaluate().Statistics.Mean);

        // In base-10 Exponential mode, phi == 8.0 occurs at elapsed = 8 * ln(10) * mean (~184.2 s).
        clock.Advance(TimeSpan.FromSeconds(180));
        Assert.IsTrue(detector.IsAvailable());
        Assert.IsLessThan(8.0, detector.GetPhi());

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.IsFalse(detector.IsAvailable());
        Assert.IsGreaterThan(8.0, detector.GetPhi());

        // Multi-day silence never produces NaN or < 0 and fails closed (IsAvailable == false).
        clock.Advance(TimeSpan.FromDays(14));
        var extremePhi = detector.GetPhi();
        Assert.IsFalse(double.IsNaN(extremePhi));
        Assert.IsGreaterThan(8.0, extremePhi);
        Assert.IsFalse(detector.IsAvailable());
    }

    [TestMethod]
    public async Task ConcurrentHeartbeatsQueriesAndResets_MaintainLinearizabilityAndAvailability()
    {
        // Phase 1: Under steady healthy heartbeats without resets, concurrent readers
        // synchronized via a Barrier must never observe torn state or false unavailability.
        var healthyClock = new FakeTimeProvider();
        var healthyDetector = new PhiAccrualFailureDetector(
            new PhiAccrualFailureDetectorOptions
            {
                Threshold = 8.0,
                MaxSampleSize = 32,
                MinStandardDeviation = TimeSpan.FromMilliseconds(20),
                InitialHeartbeatEstimate = TimeSpan.FromMilliseconds(50),
            },
            healthyClock);
        healthyDetector.RecordHeartbeat();

        var failures = new ConcurrentQueue<string>();
        const int readerCount = 4;
        using var healthyCts = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken);
        healthyCts.CancelAfter(TimeSpan.FromSeconds(15));
        var healthyToken = healthyCts.Token;

        using var startBarrier = new Barrier(readerCount + 1);
        var healthyWriteSeq = 0;
        var healthyOverlappedReaders = 0;
        var healthyReads = 0L;
        var healthyDone = 0;

        var healthyWriter = Task.Run(
            () =>
            {
                try
                {
                    startBarrier.SignalAndWait(healthyToken);
                    var step = 0;
                    while (step < 2_000
                        || Volatile.Read(ref healthyOverlappedReaders) < readerCount)
                    {
                        healthyToken.ThrowIfCancellationRequested();
                        healthyClock.Advance(TimeSpan.FromMilliseconds(10 + (step % 5)));
                        healthyDetector.RecordHeartbeat();
                        step++;
                        Volatile.Write(ref healthyWriteSeq, step);
                    }
                }
                catch
                {
                    healthyCts.Cancel();
                    throw;
                }
                finally
                {
                    Volatile.Write(ref healthyDone, 1);
                }
            },
            healthyToken);

        var healthyReaders = new Task[readerCount];
        for (var r = 0; r < healthyReaders.Length; r++)
        {
            healthyReaders[r] = Task.Run(
                () =>
                {
                    try
                    {
                        startBarrier.SignalAndWait(healthyToken);
                        var firstObservedWrite = 0;
                        var overlappedReads = 0L;
                        var markedOverlapped = false;
                        while (Volatile.Read(ref healthyDone) == 0 || !markedOverlapped)
                        {
                            healthyToken.ThrowIfCancellationRequested();
                            var observedWrite = Volatile.Read(ref healthyWriteSeq);
                            var snapshot = healthyDetector.Evaluate();
                            var phi = healthyDetector.GetPhi();
                            var available = healthyDetector.IsAvailable();
                            if (!snapshot.IsMonitoring
                                || snapshot.Elapsed is not { } elapsed
                                || elapsed < TimeSpan.Zero
                                || !double.IsFinite(snapshot.Phi)
                                || snapshot.Phi < 0.0
                                || snapshot.Phi >= 8.0
                                || !snapshot.IsAvailable
                                || !double.IsFinite(phi)
                                || phi < 0.0
                                || phi >= 8.0
                                || !available)
                            {
                                failures.Enqueue(
                                    $"Healthy read failed: snapshot={snapshot}, "
                                    + $"phi={phi}, available={available}");
                            }

                            if (observedWrite >= 1)
                            {
                                if (firstObservedWrite == 0)
                                {
                                    firstObservedWrite = observedWrite;
                                }

                                overlappedReads++;
                                if (!markedOverlapped
                                    && overlappedReads >= 200
                                    && Volatile.Read(ref healthyWriteSeq) > firstObservedWrite)
                                {
                                    markedOverlapped = true;
                                    _ = Interlocked.Increment(ref healthyOverlappedReaders);
                                }
                            }
                        }

                        _ = Interlocked.Add(ref healthyReads, overlappedReads);
                    }
                    catch
                    {
                        healthyCts.Cancel();
                        throw;
                    }
                },
                healthyToken);
        }

        await Task.WhenAll(healthyWriter, Task.WhenAll(healthyReaders)).ConfigureAwait(false);
        Assert.AreEqual(readerCount, healthyOverlappedReaders);
        Assert.IsGreaterThanOrEqualTo(readerCount * 200L, healthyReads);

        // Phase 2: Concurrent RecordHeartbeat + Reset + Evaluate never tear cross-field invariants.
        var resetClock = new FakeTimeProvider();
        var resetDetector = new PhiAccrualFailureDetector(
            new PhiAccrualFailureDetectorOptions
            {
                Threshold = 8.0,
                MaxSampleSize = 32,
            },
            resetClock);

        using var resetCts = CancellationTokenSource.CreateLinkedTokenSource(
            testContext.CancellationToken);
        resetCts.CancelAfter(TimeSpan.FromSeconds(15));
        var resetToken = resetCts.Token;

        using var resetBarrier = new Barrier(readerCount + 1);
        var resetWriteSeq = 0;
        var resetEpochSeq = 0;
        var resetOverlappedReaders = 0;
        var resetReads = 0L;
        var resetDone = 0;

        var resetWriter = Task.Run(
            () =>
            {
                try
                {
                    resetBarrier.SignalAndWait(resetToken);
                    var step = 1;
                    while (step <= 2_000
                        || Volatile.Read(ref resetEpochSeq) < 20
                        || Volatile.Read(ref resetOverlappedReaders) < readerCount)
                    {
                        resetToken.ThrowIfCancellationRequested();
                        resetClock.Advance(TimeSpan.FromMilliseconds(10 + (step % 7)));
                        resetDetector.RecordHeartbeat();
                        Volatile.Write(ref resetWriteSeq, step);
                        if (step % 37 == 0)
                        {
                            resetDetector.Reset();
                            _ = Interlocked.Increment(ref resetEpochSeq);
                        }

                        step++;
                    }
                }
                catch
                {
                    resetCts.Cancel();
                    throw;
                }
                finally
                {
                    Volatile.Write(ref resetDone, 1);
                }
            },
            resetToken);

        var resetReaders = new Task[readerCount];
        for (var r = 0; r < resetReaders.Length; r++)
        {
            resetReaders[r] = Task.Run(
                () =>
                {
                    try
                    {
                        resetBarrier.SignalAndWait(resetToken);
                        var firstObservedWrite = 0;
                        var firstObservedReset = -1;
                        var overlappedReads = 0L;
                        var markedOverlapped = false;
                        while (Volatile.Read(ref resetDone) == 0 || !markedOverlapped)
                        {
                            resetToken.ThrowIfCancellationRequested();
                            var observedWrite = Volatile.Read(ref resetWriteSeq);
                            var observedReset = Volatile.Read(ref resetEpochSeq);
                            var snapshot = resetDetector.Evaluate();

                            if (double.IsNaN(snapshot.Phi)
                                || snapshot.Phi < 0.0
                                || snapshot.IsAvailable != (snapshot.Phi < resetDetector.Threshold)
                                || snapshot.IsMonitoring != snapshot.Elapsed.HasValue)
                            {
                                failures.Enqueue($"Torn snapshot observed: {snapshot}");
                            }

                            if (!snapshot.IsMonitoring
                                && (snapshot.Phi != 0.0
                                    || !snapshot.IsAvailable
                                    || snapshot.Statistics != default))
                            {
                                failures.Enqueue($"Non-zero idle snapshot observed: {snapshot}");
                            }

                            if (snapshot.IsMonitoring && snapshot.Elapsed!.Value < TimeSpan.Zero)
                            {
                                failures.Enqueue($"Negative elapsed observed: {snapshot}");
                            }

                            var stats = snapshot.Statistics;
                            if (stats.SampleCount < 0
                                || stats.SampleCount > 32
                                || stats.Mean < TimeSpan.Zero
                                || stats.StandardDeviation < TimeSpan.Zero)
                            {
                                failures.Enqueue($"Invalid statistics observed: {stats}");
                            }

                            if (observedWrite >= 1)
                            {
                                if (firstObservedWrite == 0)
                                {
                                    firstObservedWrite = observedWrite;
                                    firstObservedReset = observedReset;
                                }

                                overlappedReads++;
                                if (!markedOverlapped
                                    && overlappedReads >= 200
                                    && Volatile.Read(ref resetWriteSeq) > firstObservedWrite
                                    && Volatile.Read(ref resetEpochSeq) > firstObservedReset)
                                {
                                    markedOverlapped = true;
                                    _ = Interlocked.Increment(ref resetOverlappedReaders);
                                }
                            }
                        }

                        _ = Interlocked.Add(ref resetReads, overlappedReads);
                    }
                    catch
                    {
                        resetCts.Cancel();
                        throw;
                    }
                },
                resetToken);
        }

        await Task.WhenAll(resetWriter, Task.WhenAll(resetReaders)).ConfigureAwait(false);
        Assert.AreEqual(readerCount, resetOverlappedReaders);
        Assert.IsGreaterThanOrEqualTo(readerCount * 200L, resetReads);
        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void SubHundredNanosecondClockAndSubMsStats_PreserveProgressAndPrecision()
    {
        var clock = new NanosecondTimeProvider();
        var detector = new PhiAccrualFailureDetector(
            new PhiAccrualFailureDetectorOptions
            {
                Threshold = 8.0,
                MinStandardDeviation = TimeSpan.FromTicks(100), // 10 us
                InitialHeartbeatEstimate = TimeSpan.FromTicks(1_000), // 100 us
            },
            clock);

        detector.RecordHeartbeat();

        // Advance in 50 ns steps (< 100 ns TimeSpan tick): each RecordHeartbeat must still
        // advance lastTimestamp rather than letting sub-100 ns deltas accumulate stale drift,
        // and GetPhi() evaluates against the unrounded 50 ns double mean in WindowMoments.
        for (var i = 0; i < 10; i++)
        {
            clock.AdvanceNanoseconds(50);
            detector.RecordHeartbeat();
        }

        Assert.AreEqual(10, detector.Evaluate().Statistics.SampleCount);
        clock.AdvanceNanoseconds(50);
        Assert.IsTrue(detector.IsAvailable());

        // Also verify 100 us intervals preserve exact sub-millisecond TimeSpan.Mean across TFMs.
        detector.Reset();
        detector.RecordHeartbeat();
        clock.AdvanceNanoseconds(100_000); // 100 us = 1000 TimeSpan ticks
        detector.RecordHeartbeat();

        Assert.AreEqual(TimeSpan.FromTicks(1_000), detector.Evaluate().Statistics.Mean);
    }

    [TestMethod]
    public void Constructor_InvalidOptionsOrTimeProvider_ThrowsArgumentOutOfRangeException()
    {
        PhiAccrualFailureDetectorOptions[] invalidCases =
        [
            new() { Threshold = 0.0 },
            new() { Threshold = -1.0 },
            new() { Threshold = 1000.01 },
            new() { Threshold = double.NaN },
            new() { Threshold = double.PositiveInfinity },
            new() { MaxSampleSize = 0 },
            new() { MinStandardDeviation = TimeSpan.Zero },
            new() { MinStandardDeviation = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1) },
            new() { MinStandardDeviation = TimeSpan.MaxValue },
            new() { AcceptableHeartbeatPause = TimeSpan.FromTicks(-1) },
            new() { AcceptableHeartbeatPause = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1) },
            new() { AcceptableHeartbeatPause = TimeSpan.MaxValue },
            new() { InitialHeartbeatEstimate = TimeSpan.Zero },
            new() { InitialHeartbeatEstimate = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1) },
            new() { InitialHeartbeatEstimate = TimeSpan.MaxValue },
            new() { Distribution = (PhiDistribution)99 },
        ];

        foreach (var invalid in invalidCases)
        {
            var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => _ = new PhiAccrualFailureDetector(invalid));
            Assert.AreEqual("options", ex.ParamName);
        }

        var providerEx = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _ = new PhiAccrualFailureDetector(timeProvider: new ZeroFrequencyTimeProvider()));
        Assert.AreEqual("timeProvider", providerEx.ParamName);
    }

    private sealed class ZeroFrequencyTimeProvider : TimeProvider
    {
        public override long TimestampFrequency => 0L;
    }

    private sealed class NanosecondTimeProvider : TimeProvider
    {
        private long timestampNanoseconds = -500;

        public override long TimestampFrequency => 1_000_000_000L;

        public override long GetTimestamp() => Interlocked.Read(ref this.timestampNanoseconds);

        public void AdvanceNanoseconds(long deltaNanoseconds)
        {
            _ = Interlocked.Add(ref this.timestampNanoseconds, deltaNanoseconds);
        }
    }
}
