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
/// Represents a linearizable point-in-time evaluation snapshot of a
/// <see cref="PhiAccrualFailureDetector"/> instance.
/// </summary>
/// <param name="Phi">
/// The computed suspicion level (<c>0.0</c> when <paramref name="IsMonitoring"/> is
/// <see langword="false"/>; otherwise a non-negative base-10 log survival value).
/// </param>
/// <param name="IsAvailable">
/// <see langword="true"/> when unmonitored or when <paramref name="Phi"/> is strictly less
/// than <see cref="PhiAccrualFailureDetector.Threshold"/>; otherwise <see langword="false"/>.
/// </param>
/// <param name="IsMonitoring">
/// <see langword="true"/> if at least one heartbeat has been recorded in the current
/// monitoring epoch; otherwise <see langword="false"/>.
/// </param>
/// <param name="Elapsed">
/// The elapsed duration since the most recent heartbeat in this snapshot when
/// <paramref name="IsMonitoring"/> is <see langword="true"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="Statistics">
/// The sliding-window inter-arrival interval statistics from the same atomic snapshot.
/// </param>
public readonly record struct PhiSnapshot(
    double Phi,
    bool IsAvailable,
    bool IsMonitoring,
    TimeSpan? Elapsed,
    HeartbeatStatistics Statistics);
