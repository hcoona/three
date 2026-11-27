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
/// Thread-safe implementation of the <c>phi</c> accrual failure detector
/// (Hayashibara et al., SRDS 2004).
/// </summary>
/// <remarks>
/// <para>
/// A detector progresses through three lifecycle states observable via <see cref="Evaluate"/>:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <b>Idle</b> (<see cref="PhiSnapshot.IsMonitoring"/> is <see langword="false"/>): before
/// the first <see cref="RecordHeartbeat"/> call (or immediately after <see cref="Reset"/>),
/// <see cref="GetPhi"/> returns <c>0.0</c>, <see cref="PhiSnapshot.Elapsed"/> is
/// <see langword="null"/>, and <see cref="IsAvailable"/> returns <see langword="true"/>
/// without polluting the sample window.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Bootstrapping</b> (<see cref="PhiSnapshot.IsMonitoring"/> is <see langword="true"/>
/// and <see cref="HeartbeatStatistics.SampleCount"/> is <c>0</c>): after the first heartbeat,
/// suspicion accrues smoothly against
/// <see cref="PhiAccrualFailureDetectorOptions.InitialHeartbeatEstimate"/> so peers that
/// crash before sending a second heartbeat are still detected.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Active</b> (<see cref="HeartbeatStatistics.SampleCount"/> is at least <c>1</c>):
/// subsequent heartbeats record real inter-arrival intervals in a bounded sliding window
/// and evaluate suspicion from a single linearizable <c>(state, clock)</c> snapshot.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed class PhiAccrualFailureDetector
{
    private const double MaxThreshold = 1000.0;
    private static readonly TimeSpan MaxOptionDuration = TimeSpan.FromDays(1);

#if NET9_0_OR_GREATER
    private readonly System.Threading.Lock sync = new();
#else
    private readonly object sync = new();
#endif

    private readonly SampleWindow window;
    private readonly TimeProvider timeProvider;
    private readonly double timestampFrequency;
    private readonly PhiDistribution distribution;
    private readonly double threshold;
    private readonly double minStdDevSeconds;
    private readonly double acceptablePauseSeconds;
    private readonly double initialEstimateSeconds;

    private bool isMonitoring;
    private long lastTimestamp;

    /// <summary>
    /// Initializes a new instance of the <see cref="PhiAccrualFailureDetector"/> class.
    /// </summary>
    /// <param name="options">
    /// Optional detector configuration. When <see langword="null"/>, default
    /// <see cref="PhiAccrualFailureDetectorOptions"/> values are used.
    /// </param>
    /// <param name="timeProvider">
    /// Optional monotonic clock source. When <see langword="null"/>,
    /// <see cref="TimeProvider.System"/> is used.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any option or <paramref name="timeProvider"/> frequency falls outside
    /// its valid domain.
    /// </exception>
    public PhiAccrualFailureDetector(
        PhiAccrualFailureDetectorOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        var resolved = options ?? new PhiAccrualFailureDetectorOptions();

        if (!PhiMath.IsFinite(resolved.Threshold)
            || resolved.Threshold <= 0.0
            || resolved.Threshold > MaxThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.Threshold,
                "Threshold must be in the range (0, 1000].");
        }

        if (resolved.MaxSampleSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.MaxSampleSize,
                "MaxSampleSize must be at least 1.");
        }

        if (resolved.MinStandardDeviation <= TimeSpan.Zero
            || resolved.MinStandardDeviation > MaxOptionDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.MinStandardDeviation,
                "MinStandardDeviation must be in (TimeSpan.Zero, TimeSpan.FromDays(1)].");
        }

        if (resolved.AcceptableHeartbeatPause < TimeSpan.Zero
            || resolved.AcceptableHeartbeatPause > MaxOptionDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.AcceptableHeartbeatPause,
                "AcceptableHeartbeatPause must be in [TimeSpan.Zero, TimeSpan.FromDays(1)].");
        }

        if (resolved.InitialHeartbeatEstimate <= TimeSpan.Zero
            || resolved.InitialHeartbeatEstimate > MaxOptionDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.InitialHeartbeatEstimate,
                "InitialHeartbeatEstimate must be in (TimeSpan.Zero, TimeSpan.FromDays(1)].");
        }

        if (resolved.Distribution is not (PhiDistribution.Normal or PhiDistribution.Exponential))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                resolved.Distribution,
                "Distribution must be Normal or Exponential.");
        }

        var provider = timeProvider ?? TimeProvider.System;
        var frequency = provider.TimestampFrequency;
        if (frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeProvider),
                frequency,
                "TimeProvider.TimestampFrequency must be strictly positive.");
        }

        this.threshold = resolved.Threshold;
        this.window = new SampleWindow(resolved.MaxSampleSize);
        this.minStdDevSeconds = resolved.MinStandardDeviation.TotalSeconds;
        this.acceptablePauseSeconds = resolved.AcceptableHeartbeatPause.TotalSeconds;
        this.initialEstimateSeconds = resolved.InitialHeartbeatEstimate.TotalSeconds;
        this.distribution = resolved.Distribution;
        this.timeProvider = provider;
        this.timestampFrequency = frequency;
    }

    /// <summary>
    /// Gets the configured suspicion threshold at or above which the monitored peer
    /// is considered unavailable.
    /// </summary>
    public double Threshold => this.threshold;

    /// <summary>
    /// Records the arrival of a heartbeat at the current timestamp reported by the
    /// configured <see cref="TimeProvider"/>.
    /// </summary>
    public void RecordHeartbeat()
    {
        lock (this.sync)
        {
            var now = this.timeProvider.GetTimestamp();
            if (!this.isMonitoring)
            {
                this.isMonitoring = true;
                this.lastTimestamp = now;
                return;
            }

            var elapsed = this.ElapsedSecondsSinceLastHeartbeat(now);
            this.window.Add(elapsed);
            if (now > this.lastTimestamp)
            {
                this.lastTimestamp = now;
            }
        }
    }

    /// <summary>
    /// Evaluates a coherent, zero-allocation point-in-time <see cref="PhiSnapshot"/>
    /// (<c>Phi</c>, <c>IsAvailable</c>, <c>IsMonitoring</c>, <c>Elapsed</c>, and
    /// <c>Statistics</c>) from a single atomic lock and clock read.
    /// </summary>
    /// <returns>
    /// A <see cref="PhiSnapshot"/> whose fields are guaranteed mutually consistent
    /// under concurrent <see cref="RecordHeartbeat"/> and <see cref="Reset"/> calls.
    /// </returns>
    public PhiSnapshot Evaluate()
    {
        this.AcquireSnapshot(out var currentlyMonitoring, out var elapsedSeconds, out var moments);
        if (!currentlyMonitoring)
        {
            return new PhiSnapshot(
                Phi: 0.0,
                IsAvailable: true,
                IsMonitoring: false,
                Elapsed: null,
                Statistics: default);
        }

        var phi = PhiMath.Evaluate(
            isMonitoring: true,
            elapsedSeconds,
            moments,
            this.distribution,
            this.initialEstimateSeconds,
            this.minStdDevSeconds,
            this.acceptablePauseSeconds);

        return new PhiSnapshot(
            Phi: phi,
            IsAvailable: phi < this.threshold,
            IsMonitoring: true,
            Elapsed: PhiMath.ToClampedTimeSpan(elapsedSeconds),
            Statistics: HeartbeatStatistics.FromMoments(moments));
    }

    /// <summary>
    /// Computes the current suspicion level (<c>phi</c>) from a linearizable atomic
    /// snapshot of detector state and current monotonic time.
    /// </summary>
    /// <returns>
    /// <c>0.0</c> before monitoring begins; otherwise a non-negative suspicion value
    /// where higher values indicate higher likelihood that the peer has failed.
    /// </returns>
    public double GetPhi()
    {
        this.AcquireSnapshot(out var currentlyMonitoring, out var elapsedSeconds, out var moments);
        return PhiMath.Evaluate(
            currentlyMonitoring,
            elapsedSeconds,
            moments,
            this.distribution,
            this.initialEstimateSeconds,
            this.minStdDevSeconds,
            this.acceptablePauseSeconds);
    }

    /// <summary>
    /// Determines whether the monitored peer is currently considered available
    /// (<c>GetPhi() &lt; Threshold</c>), failing closed to <see langword="false"/> on
    /// extreme silence (<c>+Infinity</c>) or invalid floating-point states.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when unmonitored or when <c>GetPhi() &lt; Threshold</c>;
    /// otherwise <see langword="false"/>.
    /// </returns>
    public bool IsAvailable()
    {
        return this.GetPhi() < this.threshold;
    }

    /// <summary>
    /// Clears all recorded inter-arrival samples and returns the detector to the
    /// unmonitored (<b>Idle</b>) state for a new peer incarnation epoch.
    /// </summary>
    public void Reset()
    {
        lock (this.sync)
        {
            this.isMonitoring = false;
            this.lastTimestamp = 0;
            this.window.Clear();
        }
    }

    private void AcquireSnapshot(
        out bool currentlyMonitoring,
        out double elapsedSeconds,
        out WindowMoments moments)
    {
        lock (this.sync)
        {
            currentlyMonitoring = this.isMonitoring;
            moments = this.window.Snapshot;
            if (!currentlyMonitoring)
            {
                elapsedSeconds = 0.0;
                return;
            }

            var now = this.timeProvider.GetTimestamp();
            elapsedSeconds = this.ElapsedSecondsSinceLastHeartbeat(now);
        }
    }

    private double ElapsedSecondsSinceLastHeartbeat(long now)
    {
        if (now <= this.lastTimestamp)
        {
            return 0.0;
        }

        var deltaTicks = (ulong)now - (ulong)this.lastTimestamp;
        return deltaTicks / this.timestampFrequency;
    }
}
