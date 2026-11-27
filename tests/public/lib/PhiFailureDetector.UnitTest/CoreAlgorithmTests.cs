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
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PhiFailureDetector.Internal;

namespace PhiFailureDetector.UnitTest;

/// <summary>
/// Differential Z3 formal-spec reference tests and <c>CsCheck</c> algebraic property tests
/// for the pure core algorithms (<see cref="PhiMath"/> and <see cref="SampleWindow"/>).
/// </summary>
[TestClass]
public sealed class CoreAlgorithmTests
{
    private const string DeterministicSeed = "phiDetector3";

    [TestMethod]
    public void PhiMath_MatchesZ3FormalReferenceVectorsIncludingDeepNormalTail()
    {
        // Exact reference vectors produced by scripts/verify_formal_spec.py --json.
        (double Y, double ExpectedPhi)[] normalVectors =
        [
            (-5.0, 2.1768922224231432e-08),
            (-1.0, 0.0750330321409399),
            (0.0, 0.3010299956639812),
            (1.0, 0.7995083208395598),
            (2.0, 1.6428278854871121),
            (5.0, 7.299947394460206),
            (10.0, 37.58471305287122),
            (25.0, 496.19610301322945),
            (40.0, 1989.1243168105773),
        ];

        const double mean = 2.0;
        const double stdDev = 0.5;
        var moments = new WindowMoments(
            count: 10,
            meanSeconds: mean,
            varianceSeconds: stdDev * stdDev);

        foreach (var (y, expectedPhi) in normalVectors)
        {
            var elapsed = mean + (y * stdDev);
            if (elapsed < 0.0)
            {
                // Shift mean cleanly so elapsed = mean + y * stdDev >= 0 with identical z-score y.
                var shiftedMean = 10.0;
                var shiftedMoments = new WindowMoments(10, shiftedMean, stdDev * stdDev);
                var shiftedActual = PhiMath.Evaluate(
                    isMonitoring: true,
                    elapsedSeconds: shiftedMean + (y * stdDev),
                    moments: shiftedMoments,
                    distribution: PhiDistribution.Normal,
                    initialEstimateSeconds: 1.0,
                    minStdDevSeconds: 0.01,
                    acceptablePauseSeconds: 0.0);
                Assert.IsTrue(double.IsFinite(shiftedActual));
                Assert.AreEqual(expectedPhi, shiftedActual, Math.Max(1e-12, expectedPhi * 1e-12));
                continue;
            }

            var actual = PhiMath.Evaluate(
                isMonitoring: true,
                elapsedSeconds: elapsed,
                moments: moments,
                distribution: PhiDistribution.Normal,
                initialEstimateSeconds: 1.0,
                minStdDevSeconds: 0.01,
                acceptablePauseSeconds: 0.0);

            Assert.IsTrue(double.IsFinite(actual), $"Expected finite phi at y={y}, got {actual}.");
            Assert.AreEqual(expectedPhi, actual, Math.Max(1e-12, expectedPhi * 1e-12));
        }

        (double ElapsedOverMean, double ExpectedPhi)[] exponentialVectors =
        [
            (0.0, 0.0),
            (1.0, 0.4342944819032518),
            (2.302585092994046, 1.0),
            (6.907755278982138, 3.0),
            (18.420680743952367, 8.0),
        ];

        foreach (var (ratio, expectedPhi) in exponentialVectors)
        {
            var actual = PhiMath.Evaluate(
                isMonitoring: true,
                elapsedSeconds: ratio * mean,
                moments: moments,
                distribution: PhiDistribution.Exponential,
                initialEstimateSeconds: 1.0,
                minStdDevSeconds: 0.01,
                acceptablePauseSeconds: 0.0);

            Assert.AreEqual(expectedPhi, actual, 1e-12);
        }
    }

    [TestMethod]
    public void Log1p_PreservesFullPrecisionAcrossTinyDomainAndSatisfiesLogarithmHomomorphism()
    {
        // Direct tiny-x checks where naive Math.Log(1.0 + x) loses 22-53 bits of precision
        // (including x = 8.2e-10 where .NET's double.LogP1 delegates to naive Math.Log(1.0 + x)).
        Assert.AreEqual(0.0, PhiMath.Log1p(0.0), 0.0);
        Assert.AreEqual(1e-16, PhiMath.Log1p(1e-16), 1e-30);
        Assert.AreEqual(8.199999996637999e-10, PhiMath.Log1p(8.2e-10), 1e-24);
        Assert.AreEqual(1.0, PhiMath.Log1p(Math.E - 1.0), 1e-15);

        // On [1e-16, 1e-3], 6-term Horner Mercator series has truncation error < 1.5e-22,
        // giving a sub-ULP exact mathematical oracle.
        Gen.Double[1e-16, 1e-3].Sample(
            x =>
            {
                var actual = PhiMath.Log1p(x);
                var poly = (1.0 / 5.0) - (x / 6.0);
                poly = (1.0 / 4.0) - (x * poly);
                poly = (1.0 / 3.0) - (x * poly);
                poly = (1.0 / 2.0) - (x * poly);
                var mercator = x * (1.0 - (x * poly));
                Assert.AreEqual(mercator, actual, Math.Max(1e-30, mercator * 1e-15));
            },
            seed: DeterministicSeed,
            iter: 500,
            threads: 1);

        // Across [1e-12, 1e6], verify exact group homomorphism:
        // log1p(x) + log1p(y) == log1p(x + y + x * y).
        Gen.Select(Gen.Double[1e-12, 1e6], Gen.Double[1e-12, 1e6]).Sample(
            pair =>
            {
                var (x, y) = pair;
                var lhs = PhiMath.Log1p(x) + PhiMath.Log1p(y);
                var rhs = PhiMath.Log1p(x + y + (x * y));
                Assert.AreEqual(lhs, rhs, Math.Max(1e-15, lhs * 1e-14));
            },
            seed: DeterministicSeed,
            iter: 500,
            threads: 1);
    }

    [TestMethod]
    public void PhiMath_SatisfiesMonotonicityScaleInvarianceAndPauseDominanceLaws()
    {
        var genCase = Gen.Select(
            Gen.Double[0.001, 120.0], // meanSeconds
            Gen.Double[0.0, 25.0],    // stdDevSeconds
            Gen.Double[0.001, 2.0],   // minStdDevSeconds
            Gen.Double[0.0, 10.0],    // pause1
            Gen.Double[0.0, 10.0],    // extraPause
            Gen.Double[0.0, 300.0],   // t1
            Gen.Double[0.0, 300.0],   // deltaT
            Gen.Double[0.01, 100.0]); // scale k

        genCase.Sample(
            tuple =>
            {
                var (mean, stdDev, minStdDev, pause1, extraPause, t1, deltaT, scale) = tuple;
                var t2 = t1 + deltaT;
                var pause2 = pause1 + extraPause;
                var moments = new WindowMoments(16, mean, stdDev * stdDev);
                var scaledMoments = new WindowMoments(
                    16,
                    mean * scale,
                    (stdDev * scale) * (stdDev * scale));

                foreach (var dist in new[] { PhiDistribution.Normal, PhiDistribution.Exponential })
                {
                    var phi1 = PhiMath.Evaluate(true, t1, moments, dist, 1.0, minStdDev, pause1);
                    var phi2 = PhiMath.Evaluate(true, t2, moments, dist, 1.0, minStdDev, pause1);
                    var phiPaused = PhiMath.Evaluate(
                        true,
                        t1,
                        moments,
                        dist,
                        1.0,
                        minStdDev,
                        pause2);
                    var phiScaled = PhiMath.Evaluate(
                        true,
                        t1 * scale,
                        scaledMoments,
                        dist,
                        1.0 * scale,
                        minStdDev * scale,
                        pause1 * scale);

                    Assert.IsTrue(double.IsFinite(phi1) && phi1 >= 0.0);
                    Assert.IsTrue(double.IsFinite(phi2) && phi2 >= 0.0);
                    Assert.IsGreaterThanOrEqualTo(phi1 - 1e-12, phi2);
                    Assert.IsLessThanOrEqualTo(phi1 + 1e-12, phiPaused);

                    var scaleTol = Math.Max(1e-11, Math.Abs(phi1) * 1e-11);
                    Assert.AreEqual(phi1, phiScaled, scaleTol);
                }
            },
            seed: DeterministicSeed,
            iter: 500,
            threads: 1);
    }

    [TestMethod]
    public void SampleWindow_ResistsCancellationAndPostOutlierEvictionAcrossWraparounds()
    {
        // 1.0 s vs 1.0 s + 1 ns across 5 full window cycles
        // (where unshifted E[X^2] - E[X]^2 collapses to 0).
        var lowJitterWindow = new SampleWindow(capacity: 8);
        for (var i = 0; i < 40; i++)
        {
            lowJitterWindow.Add(i % 2 == 0 ? 1.0 : 1.0 + 1e-9);
        }

        var lowJitterStats = lowJitterWindow.Snapshot;
        Assert.AreEqual(8, lowJitterStats.Count);
        Assert.AreEqual(1.0 + 0.5e-9, lowJitterStats.MeanSeconds, 1e-15);
        Assert.AreEqual(0.25e-18, lowJitterStats.VarianceSeconds, 1e-21);

        // Post-outlier eviction regression ([86400, 1, 1, 1.001] at capacity 3 and
        // cascading multi-scale outliers): once outliers slide out, live variance
        // must match the two-pass oracle instead of collapsing to 0.
        var outlierWindow = new SampleWindow(capacity: 3);
        foreach (var sample in new[] { 86_400.0, 1.0, 1.0, 1.001 })
        {
            outlierWindow.Add(sample);
        }

        var postOutlier = outlierWindow.Snapshot;
        const double expectedPostOutlierMean = (1.0 + 1.0 + 1.001) / 3.0;
        const double expectedPostOutlierVar = 2.0e-6 / 9.0;
        Assert.AreEqual(3, postOutlier.Count);
        Assert.AreEqual(expectedPostOutlierMean, postOutlier.MeanSeconds, 1e-15);
        Assert.AreEqual(expectedPostOutlierVar, postOutlier.VarianceSeconds, 1e-18);

        var cascadeWindow = new SampleWindow(capacity: 3);
        foreach (var sample in new[] { 1e9, 1e6, 1e3, 1.0, 1.0, 1.001 })
        {
            cascadeWindow.Add(sample);
        }

        Assert.AreEqual(expectedPostOutlierVar, cascadeWindow.Snapshot.VarianceSeconds, 1e-18);

        // Random capacity, wraparound evictions, clamping, and Clear() vs two-pass oracle.
        var genOps = Gen.Select(
            Gen.Int[1, 8],
            Gen.Double[-0.5, 500.0].Array[1, 60],
            Gen.Int[0, 60]);

        genOps.Sample(
            tuple =>
            {
                var (capacity, samples, clearIndex) = tuple;
                var window = new SampleWindow(capacity);
                var oracle = new Queue<double>(capacity);

                for (var i = 0; i < samples.Length; i++)
                {
                    if (i == clearIndex)
                    {
                        window.Clear();
                        oracle.Clear();
                        Assert.AreEqual(0, window.Snapshot.Count);
                    }

                    var raw = samples[i];
                    var clamped = raw > 0.0 ? raw : 0.0;
                    window.Add(raw);

                    if (oracle.Count == capacity)
                    {
                        _ = oracle.Dequeue();
                    }

                    oracle.Enqueue(clamped);

                    var actual = window.Snapshot;
                    var expectedMean = oracle.Average();
                    var expectedVariance = oracle.Sum(
                        v => (v - expectedMean) * (v - expectedMean)) / oracle.Count;

                    Assert.AreEqual(oracle.Count, actual.Count);
                    Assert.IsGreaterThanOrEqualTo(0.0, actual.MeanSeconds);
                    Assert.IsGreaterThanOrEqualTo(0.0, actual.VarianceSeconds);
                    Assert.AreEqual(
                        expectedMean,
                        actual.MeanSeconds,
                        Math.Max(1e-11, expectedMean * 1e-11));
                    Assert.AreEqual(
                        expectedVariance,
                        actual.VarianceSeconds,
                        Math.Max(1e-9, expectedVariance * 1e-9));
                }
            },
            seed: DeterministicSeed,
            iter: 400,
            threads: 1);
    }
}
