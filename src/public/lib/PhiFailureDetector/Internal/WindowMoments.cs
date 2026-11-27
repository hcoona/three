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

namespace PhiFailureDetector.Internal;

/// <summary>
/// Internal point-in-time snapshot of raw floating-point window moments in seconds.
/// </summary>
internal readonly struct WindowMoments
{
    internal WindowMoments(int count, double meanSeconds, double varianceSeconds)
    {
        this.Count = count;
        this.MeanSeconds = meanSeconds;
        this.VarianceSeconds = varianceSeconds;
    }

    internal int Count { get; }

    internal double MeanSeconds { get; }

    internal double VarianceSeconds { get; }
}
