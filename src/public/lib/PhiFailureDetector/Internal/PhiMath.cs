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

namespace PhiFailureDetector.Internal;

/// <summary>
/// Pure, stateless floating-point evaluators for the <c>phi</c> accrual failure
/// detector survival functions verified in <c>scripts/verify_formal_spec.py</c>.
/// </summary>
internal static class PhiMath
{
    internal const double Log10E = 0.43429448190325182765;

    private const double LogisticC1 = 1.5976;
    private const double LogisticC2 = 0.070566;
    private const double BootstrapStdDevRatio = 0.25;
    private const double MaxTimeSpanTicksDouble = long.MaxValue;

    internal static bool IsFinite(double value)
    {
#if NET8_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        return double.IsFinite(value);
#else
        return !double.IsNaN(value) && !double.IsInfinity(value);
#endif
    }

    internal static TimeSpan ToClampedTimeSpan(double seconds)
    {
        if (seconds <= 0.0 || double.IsNaN(seconds))
        {
            return TimeSpan.Zero;
        }

        var ticks = seconds * TimeSpan.TicksPerSecond;
        if (ticks >= MaxTimeSpanTicksDouble)
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromTicks((long)Math.Round(ticks));
    }

    internal static double Evaluate(
        bool isMonitoring,
        double elapsedSeconds,
        WindowMoments moments,
        PhiDistribution distribution,
        double initialEstimateSeconds,
        double minStdDevSeconds,
        double acceptablePauseSeconds)
    {
        if (!isMonitoring)
        {
            return 0.0;
        }

        if (distribution == PhiDistribution.Exponential)
        {
            var expMeanSeconds = moments.Count == 0
                ? initialEstimateSeconds
                : moments.MeanSeconds;
            return Exponential(elapsedSeconds, expMeanSeconds, acceptablePauseSeconds);
        }

        double meanSeconds;
        double stdDevSeconds;
        if (moments.Count == 0)
        {
            meanSeconds = initialEstimateSeconds;
            stdDevSeconds = initialEstimateSeconds * BootstrapStdDevRatio;
        }
        else
        {
            meanSeconds = moments.MeanSeconds;
            stdDevSeconds = Math.Sqrt(moments.VarianceSeconds);
        }

        return Normal(
            elapsedSeconds,
            meanSeconds,
            stdDevSeconds,
            minStdDevSeconds,
            acceptablePauseSeconds);
    }

    internal static double Exponential(
        double elapsedSeconds,
        double meanSeconds,
        double acceptablePauseSeconds)
    {
        if (elapsedSeconds <= 0.0)
        {
            return 0.0;
        }

        var effectiveMean = meanSeconds + acceptablePauseSeconds;
        if (effectiveMean <= 0.0)
        {
            return double.PositiveInfinity;
        }

        return (elapsedSeconds / effectiveMean) * Log10E;
    }

    internal static double Normal(
        double elapsedSeconds,
        double meanSeconds,
        double stdDevSeconds,
        double minStdDevSeconds,
        double acceptablePauseSeconds)
    {
        if (elapsedSeconds <= 0.0)
        {
            return 0.0;
        }

        var effectiveMean = meanSeconds + acceptablePauseSeconds;
        var effectiveStdDev = Math.Max(stdDevSeconds, minStdDevSeconds);
        if (effectiveStdDev <= 0.0)
        {
            return elapsedSeconds > effectiveMean ? double.PositiveInfinity : 0.0;
        }

        var y = (elapsedSeconds - effectiveMean) / effectiveStdDev;
        var z = y * (LogisticC1 + (LogisticC2 * y * y));
        var softplus = z > 0.0
            ? z + Log1p(Math.Exp(-z))
            : Log1p(Math.Exp(z));

        return softplus * Log10E;
    }

    /// <summary>
    /// Computes <c>ln(1 + x)</c> accurately for <c>x &gt;= 0</c>, avoiding catastrophic
    /// cancellation when <c>x</c> is much smaller than <c>1.0</c> (Goldberg, 1991, Theorem 4).
    /// </summary>
    /// <remarks>
    /// Used unconditionally across all target frameworks because <c>double.LogP1</c> in
    /// .NET delegates to <c>Math.Log(1.0 + x)</c> for small non-denormal inputs (for example
    /// <c>x = 8.2e-10</c>), losing ~22 bits of relative precision.
    /// </remarks>
    internal static double Log1p(double x)
    {
        var sum = 1.0 + x;
        if (sum == 1.0)
        {
            return x;
        }

        return Math.Log(sum) * (x / (sum - 1.0));
    }
}
