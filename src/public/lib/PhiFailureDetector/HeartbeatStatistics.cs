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
using PhiFailureDetector.Internal;

namespace PhiFailureDetector;

/// <summary>
/// Represents an immutable point-in-time snapshot of observed heartbeat inter-arrival
/// interval statistics within a <see cref="PhiAccrualFailureDetector"/> sliding window.
/// </summary>
/// <param name="SampleCount">
/// The number of heartbeat inter-arrival intervals currently held in the window.
/// </param>
/// <param name="Mean">
/// The sample mean of observed inter-arrival intervals.
/// </param>
/// <param name="StandardDeviation">
/// The population standard deviation of observed inter-arrival intervals.
/// </param>
public readonly record struct HeartbeatStatistics(
    int SampleCount,
    TimeSpan Mean,
    TimeSpan StandardDeviation)
{
    internal static HeartbeatStatistics FromMoments(WindowMoments moments)
    {
        if (moments.Count == 0)
        {
            return default;
        }

        return new HeartbeatStatistics(
            moments.Count,
            PhiMath.ToClampedTimeSpan(moments.MeanSeconds),
            PhiMath.ToClampedTimeSpan(Math.Sqrt(moments.VarianceSeconds)));
    }
}
