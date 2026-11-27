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

namespace PhiFailureDetector;

/// <summary>
/// Configures parameters for a <see cref="PhiAccrualFailureDetector"/> instance.
/// </summary>
public sealed record PhiAccrualFailureDetectorOptions
{
    /// <summary>
    /// Gets the suspicion threshold at or above which a monitored node is considered
    /// unavailable (<c>GetPhi() &gt;= Threshold</c>). Must be in <c>(0, 1000]</c>.
    /// Defaults to <c>8.0</c>.
    /// </summary>
    public double Threshold { get; init; } = 8.0;

    /// <summary>
    /// Gets the maximum number of heartbeat inter-arrival intervals retained in the
    /// sliding window. Must be at least <c>1</c>. Defaults to <c>1000</c>.
    /// </summary>
    public int MaxSampleSize { get; init; } = 1000;

    /// <summary>
    /// Gets the minimum standard deviation applied when evaluating the
    /// <see cref="PhiDistribution.Normal"/> model, preventing zero-jitter intervals
    /// from causing hair-trigger false suspicions. Must be strictly positive and at
    /// most <c>1 day</c>. Defaults to <c>100 ms</c>.
    /// </summary>
    public TimeSpan MinStandardDeviation { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets the acceptable pause duration added to the expected inter-arrival mean to
    /// absorb transient GC pauses or scheduling stalls without false suspicion. Must be
    /// between <see cref="TimeSpan.Zero"/> and <c>1 day</c> inclusive.
    /// Defaults to <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan AcceptableHeartbeatPause { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Gets the expected inter-arrival interval used during bootstrapping after the
    /// first heartbeat arrives and before a second heartbeat establishes an observed
    /// interval. Must be strictly positive and at most <c>1 day</c>.
    /// Defaults to <c>1 s</c>.
    /// </summary>
    public TimeSpan InitialHeartbeatEstimate { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the arrival distribution model used to compute <c>phi</c>.
    /// Defaults to <see cref="PhiDistribution.Normal"/>.
    /// </summary>
    public PhiDistribution Distribution { get; init; } = PhiDistribution.Normal;
}
