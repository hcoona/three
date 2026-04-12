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

namespace PhiFailureDetector
{
    /// <summary>
    /// Implements the Phi Accrual Failure Detector algorithm for monitoring node health
    /// in distributed systems. The detector calculates a suspicion level (phi value)
    /// based on the arrival pattern of heartbeat messages.
    /// </summary>
    /// <remarks>
    /// The phi value represents the likelihood that a monitored node has failed.
    /// Higher phi values indicate higher suspicion of failure. The algorithm adapts
    /// to network conditions and varying heartbeat intervals.
    /// </remarks>
    public class PhiFailureDetector
    {
        private readonly LongIntervalHistoryCollection arrivalWindow;
        private readonly long initialHeartbeatInterval;
        private readonly TimeProvider timeProvider;
        private readonly PhiFunc phiFunc;

        private long last;

        /// <summary>
        /// Initializes a new instance of the <see cref="PhiFailureDetector"/> class.
        /// </summary>
        /// <param name="capacity">The maximum number of heartbeat intervals to track for statistical calculations.</param>
        /// <param name="initialHeartbeatInterval">The initial expected heartbeat interval in milliseconds, used until enough samples are collected.</param>
        /// <param name="timeProvider">The time provider used to get current timestamps.</param>
        /// <param name="phiFunc">The function used to calculate the phi value based on timing statistics.</param>
        public PhiFailureDetector(
            int capacity,
            long initialHeartbeatInterval,
            TimeProvider timeProvider,
            PhiFunc phiFunc)
        {
            this.arrivalWindow = new LongIntervalHistoryCollection(capacity);
            this.initialHeartbeatInterval = initialHeartbeatInterval;
            this.timeProvider = timeProvider;
            this.phiFunc = phiFunc;
        }

        /// <summary>
        /// Represents a function that calculates the phi value (suspicion level) based on timing statistics.
        /// </summary>
        /// <param name="timestamp">The current timestamp.</param>
        /// <param name="lastTimestamp">The timestamp of the last received heartbeat.</param>
        /// <param name="statistics">Statistical information about historical heartbeat intervals.</param>
        /// <returns>The calculated phi value representing the suspicion level of node failure.</returns>
        public delegate double PhiFunc(
            long timestamp, long lastTimestamp, IWithStatistics statistics);

        /// <summary>
        /// Calculates the phi value using an exponential distribution model.
        /// This implementation is appropriate for Poisson processes where events
        /// (heartbeats) occur at random intervals.
        /// </summary>
        /// <param name="nowTimestamp">The current timestamp.</param>
        /// <param name="lastTimestamp">The timestamp of the last received heartbeat.</param>
        /// <param name="statistics">Statistical information about historical heartbeat intervals.</param>
        /// <returns>The calculated phi value based on exponential distribution.</returns>
        /// <remarks>
        /// <para>
        /// Regular message transmissions experiencing typical random jitter will follow a normal
        /// distribution, but since gossip messages from endpoint A to endpoint B are sent at random
        /// intervals, they likely make up a Poisson process, making the exponential distribution
        /// appropriate.
        /// </para>
        /// <para>
        /// Mathematical basis:
        /// <br/>P_later(t) = 1 - F(t)
        /// <br/>P_later(t) = 1 - (1 - e^(-Lt))
        /// <br/>The maximum likelihood estimation for the rate parameter L is given by 1/mean
        /// <br/>P_later(t) = 1 - (1 - e^(-t/mean))
        /// <br/>P_later(t) = e^(-t/mean)
        /// <br/>phi(t) = -log10(P_later(t))
        /// <br/>phi(t) = -log10(e^(-t/mean))
        /// <br/>phi(t) = -log(e^(-t/mean)) / log(10)
        /// <br/>phi(t) = (t/mean) / log(10)
        /// <br/>phi(t) = 0.4342945 * t/mean.
        /// </para>
        /// <para>
        /// Reference: <see href="https://issues.apache.org/jira/browse/CASSANDRA-2597"/>.
        /// </para>
        /// </remarks>
        public static double Exponential(
            long nowTimestamp, long lastTimestamp, IWithStatistics statistics)
        {
            var duration = nowTimestamp - lastTimestamp;
            return duration / statistics.Avg;
        }

        /// <summary>
        /// Calculates the phi value using a normal distribution model with logistic approximation.
        /// This implementation is suitable for heartbeat patterns that follow a normal distribution.
        /// </summary>
        /// <param name="nowTimestamp">The current timestamp.</param>
        /// <param name="lastTimestamp">The timestamp of the last received heartbeat.</param>
        /// <param name="statistics">Statistical information about historical heartbeat intervals.</param>
        /// <returns>The calculated phi value based on normal distribution.</returns>
        /// <remarks>
        /// <para>
        /// Calculation of phi, derived from the Cumulative distribution function for
        /// N(mean, stdDeviation) normal distribution, given by:
        /// <br/>1.0 / (1.0 + math.exp(-y * (1.5976 + 0.070566 * y * y)))
        /// <br/>where y = (x - mean) / standard_deviation.
        /// </para>
        /// <para>
        /// This is an approximation defined in β Mathematics Handbook (Logistic approximation).
        /// Error is 0.00014 at +- 3.16. The calculated value is equivalent to -log10(1 - CDF(y)).
        /// </para>
        /// <para>
        /// Reference: <see href="https://github.com/akka/akka/blob/master/akka-remote/src/main/scala/akka/remote/PhiAccrualFailureDetector.scala"/>.
        /// </para>
        /// </remarks>
        public static double Normal(
            long nowTimestamp, long lastTimestamp, IWithStatistics statistics)
        {
            var duration = nowTimestamp - lastTimestamp;
            var y = (duration - statistics.Avg) / statistics.StdDeviation;
            var exp = Math.Exp(-y * (1.5976 + (0.070566 * y * y)));
            if (duration > statistics.Avg)
            {
                return -Math.Log10(exp / (1 + exp));
            }
            else
            {
                return -Math.Log10(1 - (1 / (1 + exp)));
            }
        }

        /// <summary>
        /// Calculates the current phi value (suspicion level) based on the time elapsed
        /// since the last heartbeat and historical timing statistics.
        /// </summary>
        /// <returns>
        /// The phi value representing the suspicion level of node failure.
        /// Higher values indicate higher suspicion of failure.
        /// </returns>
        /// <remarks>
        /// This method uses the configured phi function (exponential or normal distribution)
        /// to calculate the suspicion level based on current timing conditions.
        /// </remarks>
        public double Phi()
        {
            return this.phiFunc(
                this.timeProvider.GetTimestamp(),
                this.last,
                this.arrivalWindow);
        }

        /// <summary>
        /// Reports that a heartbeat has been received from the monitored node.
        /// This method updates the internal statistics and timing information
        /// used for phi value calculations.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For the first heartbeat, the initial heartbeat interval is used.
        /// For subsequent heartbeats, the actual interval since the last heartbeat
        /// is calculated and added to the statistical history.
        /// </para>
        /// <para>
        /// This method should be called each time a heartbeat message is received
        /// from the monitored node to maintain accurate failure detection.
        /// </para>
        /// </remarks>
        public void Report()
        {
            var now = this.timeProvider.GetTimestamp();

            if (this.arrivalWindow.Count == 0)
            {
                this.arrivalWindow.Enqueue(this.initialHeartbeatInterval);
            }
            else
            {
                var interval = now - this.last;
                this.arrivalWindow.Enqueue(interval);
            }

            this.last = now;
        }
    }
}
