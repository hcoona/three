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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace PhiFailureDetector
{
    /// <summary>
    /// A bounded collection that maintains a rolling window of long interval values
    /// and provides statistical calculations (average, variance, standard deviation).
    /// When the collection reaches its capacity, the oldest values are automatically removed.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1010:Generic interface should also be implemented",
        Justification = "By-design")]
    public class LongIntervalHistoryCollection :
        IEnumerable<long>, ICollection, IEnumerable, IWithStatistics
    {
        private readonly Queue<long> queue;
        private readonly int capacity;

        private long sum;
        private long squaredSum;
        private double avg;

        /// <summary>
        /// Initializes a new instance of the <see cref="LongIntervalHistoryCollection"/> class with the specified capacity.
        /// </summary>
        /// <param name="capacity">The maximum number of values that can be stored in the collection.</param>
        public LongIntervalHistoryCollection(int capacity)
        {
            this.capacity = capacity;
            this.queue = new Queue<long>(capacity);
        }

        /// <inheritdoc/>
        public long Sum => this.sum;

        /// <inheritdoc/>
        public double Avg => this.avg;

        /// <inheritdoc/>
        public double Variance =>
            ((double)this.squaredSum / this.Count) - (this.avg * this.avg);

        /// <inheritdoc/>
        public double StdDeviation => Math.Sqrt(this.Variance);

        /// <inheritdoc/>
        public int Count => this.queue.Count;

        /// <inheritdoc/>
        public object SyncRoot => ((ICollection)this.queue).SyncRoot;

        /// <inheritdoc/>
        public bool IsSynchronized => ((ICollection)this.queue).IsSynchronized;

        /// <summary>
        /// Removes and returns the value at the beginning of the collection.
        /// </summary>
        /// <returns>The value that was removed from the beginning of the collection.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the collection is empty.</exception>
        /// <remarks>
        /// This method also updates the internal statistical calculations after removing the value.
        /// </remarks>
        public long Dequeue()
        {
            var value = this.queue.Dequeue();
            this.sum -= value;
            this.squaredSum -= value * value;
            this.avg = this.sum / this.Count;
            return value;
        }

        /// <summary>
        /// Adds a value to the end of the collection. If the collection is at capacity,
        /// the oldest value is automatically removed before adding the new value.
        /// </summary>
        /// <param name="item">The value to add to the collection.</param>
        /// <remarks>
        /// This method maintains the rolling window behavior and updates all statistical calculations.
        /// </remarks>
        public void Enqueue(long item)
        {
            if (this.queue.Count == this.capacity)
            {
                var value = this.queue.Dequeue();
                this.sum -= value;
                this.squaredSum -= value * value;
            }

            this.queue.Enqueue(item);
            this.sum += item;
            this.squaredSum += item * item;
            this.avg = this.sum / this.Count;
        }

        /// <summary>
        /// Removes all values from the collection.
        /// </summary>
        /// <remarks>
        /// After calling this method, the collection will be empty and all statistical values will be reset.
        /// </remarks>
        public void Clear()
        {
            this.queue.Clear();
            this.sum = 0;
            this.squaredSum = 0;
            this.avg = 0;
        }

        /// <summary>
        /// Determines whether the collection contains a specific value.
        /// </summary>
        /// <param name="item">The value to locate in the collection.</param>
        /// <returns>true if the value is found in the collection; otherwise, false.</returns>
        public bool Contains(long item) => this.queue.Contains(item);

        /// <summary>
        /// Copies the collection values to an existing one-dimensional array, starting at the specified array index.
        /// </summary>
        /// <param name="array">The one-dimensional array that is the destination of the values copied from the collection.</param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
        /// <exception cref="ArgumentNullException">Thrown when array is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when arrayIndex is less than zero.</exception>
        /// <exception cref="ArgumentException">Thrown when the destination array is too small.</exception>
        public void Copylongo(long[] array, int arrayIndex) =>
            this.queue.CopyTo(array, arrayIndex);

        /// <summary>
        /// Returns the value at the beginning of the collection without removing it.
        /// </summary>
        /// <returns>The value at the beginning of the collection.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the collection is empty.</exception>
        public long Peek() => this.queue.Peek();

        /// <summary>
        /// Copies the collection values to a new array.
        /// </summary>
        /// <returns>A new array containing all the values in the collection.</returns>
        public long[] ToArray() => this.queue.ToArray();

        /// <inheritdoc/>
        public void CopyTo(Array array, int index)
        {
            ((ICollection)this.queue).CopyTo(array, index);
        }

        /// <inheritdoc/>
        public IEnumerator<long> GetEnumerator()
        {
            return ((IEnumerable<long>)this.queue).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<long>)this.queue).GetEnumerator();
        }
    }
}
