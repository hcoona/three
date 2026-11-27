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
/// Pre-allocated, zero-allocation sliding window over <c>double</c> inter-arrival
/// intervals in seconds using a two-stack queue of Chan pairwise variance aggregates.
/// </summary>
/// <remarks>
/// Maintaining prefix aggregates over <c>front</c> (oldest at top) and <c>back</c>
/// (newest at top) via Chan et al.'s parallel variance combination achieves strict
/// <c>O(1)</c> amortized updates and <c>O(1)</c> queries without ever subtracting
/// squared sums. When an extreme outlier is evicted from the window, no remaining
/// prefix aggregate in either stack ever referenced that outlier, preventing post-outlier
/// variance collapse.
/// </remarks>
internal sealed class SampleWindow
{
    private readonly ChanAggregate[] front;
    private readonly double[] backValues;
    private readonly ChanAggregate[] backAggregates;
    private int frontCount;
    private int backCount;

    internal SampleWindow(int capacity)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
#else
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
#endif

        this.front = new ChanAggregate[capacity];
        this.backValues = new double[capacity];
        this.backAggregates = new ChanAggregate[capacity];
    }

    internal int Capacity => this.backValues.Length;

    internal WindowMoments Snapshot
    {
        get
        {
            var totalCount = this.frontCount + this.backCount;
            if (totalCount == 0)
            {
                return default;
            }

            ChanAggregate combined;
            if (this.frontCount == 0)
            {
                combined = this.backAggregates[this.backCount - 1];
            }
            else if (this.backCount == 0)
            {
                combined = this.front[this.frontCount - 1];
            }
            else
            {
                combined = ChanAggregate.Combine(
                    this.front[this.frontCount - 1],
                    this.backAggregates[this.backCount - 1]);
            }

            return new WindowMoments(
                combined.Count,
                combined.Mean,
                combined.M2 / combined.Count);
        }
    }

    internal void Add(double intervalSeconds)
    {
        var sample = intervalSeconds > 0.0 ? intervalSeconds : 0.0;
        var capacity = this.backValues.Length;

        if (this.frontCount + this.backCount == capacity)
        {
            if (this.frontCount > 0)
            {
                this.frontCount--;
            }
            else
            {
                // Oldest sample is backValues[0]; discard it and flip
                // backValues[capacity - 1 .. 1] into front with fresh aggregates.
                var acc = default(ChanAggregate);
                for (var i = this.backCount - 1; i >= 1; i--)
                {
                    acc = ChanAggregate.Combine(acc, ChanAggregate.Singleton(this.backValues[i]));
                    this.front[this.frontCount++] = acc;
                }

                this.backCount = 0;
            }
        }

        var single = ChanAggregate.Singleton(sample);
        var nextAggregate = this.backCount == 0
            ? single
            : ChanAggregate.Combine(this.backAggregates[this.backCount - 1], single);

        this.backValues[this.backCount] = sample;
        this.backAggregates[this.backCount] = nextAggregate;
        this.backCount++;
    }

    internal void Clear()
    {
        this.frontCount = 0;
        this.backCount = 0;
    }

    private readonly struct ChanAggregate
    {
        internal ChanAggregate(int count, double mean, double m2)
        {
            this.Count = count;
            this.Mean = mean;
            this.M2 = m2;
        }

        internal int Count { get; }

        internal double Mean { get; }

        internal double M2 { get; }

        internal static ChanAggregate Singleton(double sample)
        {
            return new ChanAggregate(1, sample, 0.0);
        }

        internal static ChanAggregate Combine(ChanAggregate left, ChanAggregate right)
        {
            if (left.Count == 0)
            {
                return right;
            }

            if (right.Count == 0)
            {
                return left;
            }

            var count = left.Count + right.Count;
            var delta = right.Mean - left.Mean;
            var rightWeight = (double)right.Count / count;
            var mean = Math.Max(0.0, left.Mean + (delta * rightWeight));
            var m2 = left.M2 + right.M2 + (delta * delta * (left.Count * rightWeight));
            return new ChanAggregate(count, mean, m2);
        }
    }
}
