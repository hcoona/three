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

namespace PhiFailureDetector;

/// <summary>
/// Specifies the probability distribution used to model heartbeat inter-arrival times
/// when computing the suspicion level (<c>phi</c>).
/// </summary>
public enum PhiDistribution
{
    /// <summary>
    /// Models heartbeat inter-arrival times with a Gaussian (Normal) distribution
    /// subject to a minimum standard-deviation floor, suited to periodic heartbeats
    /// with network and scheduling jitter (Hayashibara et al., SRDS 2004).
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Models heartbeat inter-arrival times with a memoryless Exponential distribution,
    /// suited to gossip or Poisson arrival processes (Cassandra / CASSANDRA-2597).
    /// </summary>
    Exponential = 1,
}
