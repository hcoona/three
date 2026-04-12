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
using System.Linq;

#if !NETSTANDARD2_0
using System.Runtime.CompilerServices;
#endif

namespace IO.Github.Hcoona.Collections
{
    /// <summary>
    /// A circular list implementation that provides fixed-capacity storage with overflow behavior.
    /// When capacity is exceeded, new items overwrite the oldest items in a circular fashion.
    /// </summary>
    /// <typeparam name="T">The type of elements in the circular list.</typeparam>
    public class CircularList<T> : IList<T>
    {
        private readonly T[] items;
        private int count;
        private int startIndex;

        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="CircularList{T}"/> class with the specified capacity.
        /// </summary>
        /// <param name="capacity">The maximum number of items the circular list can hold.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when capacity is negative.
        /// </exception>
        public CircularList(int capacity)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "Capacity must be non-negative.");
            }

            this.items = new T[capacity];
            this.count = 0;
            this.startIndex = 0;
        }

        /// <summary>
        /// Occurs when an item is overwritten due to capacity overflow.
        /// </summary>
#if !NETSTANDARD2_0 && !NET462
        public event EventHandler<T>? OnOverflow;
#else
        public event EventHandler<T> OnOverflow;
#endif

        /// <summary>
        /// Gets the maximum number of items this circular list can hold.
        /// </summary>
        public int Capacity => this.items.Length;

        /// <inheritdoc/>
        public int Count => this.count;

        /// <inheritdoc/>
        public bool IsReadOnly => false;

        /// <inheritdoc/>
        public T this[int index]
        {
            get => this.items[this.GetPhysicalIndex(index)];
            set => this.items[this.GetPhysicalIndex(index)] = value;
        }

        /// <summary>
        /// Adds an item to the circular list. If capacity is exceeded, overwrites the oldest item.
        /// </summary>
        /// <param name="item">The item to add to the circular list.</param>
        public void Add(T item)
        {
            if (this.Count == this.Capacity)
            {
                var overwrittenItem = this[0];
                this[0] = item;
                this.startIndex = (this.startIndex + 1) % this.Capacity;

                this.OnOverflow?.Invoke(this, overwrittenItem);
            }
            else
            {
                this[this.Count] = item;
                this.count++;
            }
        }

        /// <inheritdoc/>
        public bool Contains(T item)
        {
            if (this.Count == this.Capacity)
            {
                return Array.Exists(
                    this.items,
                    element => EqualityComparer<T>.Default.Equals(element, item));
            }
            else
            {
                return this.AsEnumerable().Contains(item);
            }
        }

        /// <summary>
        /// Removes the first occurrence of a specific item from the circular list.
        /// This operation is not supported for circular lists.
        /// </summary>
        /// <param name="item">The item to remove from the circular list.</param>
        /// <returns>This operation is not supported for circular lists.</returns>
        /// <exception cref="NotSupportedException">
        /// Always thrown as removal is not supported.
        /// </exception>
        public bool Remove(T item)
        {
            throw new NotSupportedException(
                "Remove operation is not supported for circular lists.");
        }

        /// <inheritdoc/>
        public void Clear()
        {
            this.count = 0;
            this.startIndex = 0;
        }

        /// <summary>
        /// Returns a span view of the circular list contents.
        /// Note: This span may not be contiguous if the circular buffer has wrapped.
        /// </summary>
        /// <returns>A span representing the current contents of the circular list.</returns>
        public ReadOnlySpan<T> AsSpan()
        {
            if (this.Count == 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            // If the data doesn't wrap around, return a contiguous span
            if (this.startIndex + this.Count <= this.Capacity)
            {
                return new ReadOnlySpan<T>(this.items, this.startIndex, this.Count);
            }

            // Data wraps around - we need to copy to provide a contiguous view
            var result = new T[this.Count];
            for (int i = 0; i < this.Count; i++)
            {
                result[i] = this[i];
            }

            return new ReadOnlySpan<T>(result);
        }

        /// <inheritdoc/>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array is null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    "Array index must be non-negative.");
            }

            if (arrayIndex + this.Count > array.Length)
            {
                throw new ArgumentException(
                    "Destination array is not large enough to copy all items.");
            }

            for (int i = 0; i < this.Count; i++)
            {
                array[arrayIndex + i] = this[i];
            }
        }

        /// <inheritdoc/>
        public int IndexOf(T item)
        {
            for (int i = 0; i < this.Count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(this[i], item))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Inserts an item at the specified index.
        /// This operation is not supported for circular lists.
        /// </summary>
        /// <param name="index">The zero-based index at which to insert the item.</param>
        /// <param name="item">The item to insert.</param>
        /// <exception cref="NotSupportedException">
        /// Always thrown as insertion at arbitrary positions is not supported.
        /// </exception>
        public void Insert(int index, T item)
        {
            throw new NotSupportedException(
                "Insert operation is not supported for circular lists. " +
                "Use Add() to append items.");
        }

        /// <summary>
        /// Removes the item at the specified index.
        /// This operation is not supported for circular lists.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        /// <exception cref="NotSupportedException">
        /// Always thrown as removal at arbitrary positions is not supported.
        /// </exception>
        public void RemoveAt(int index)
        {
            throw new NotSupportedException(
                "RemoveAt operation is not supported for circular lists. " +
                "Use Clear() to reset the list.");
        }

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < this.Count; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        /// <summary>
        /// Converts logical index to physical array index, handling circular wrapping.
        /// </summary>
        /// <param name="logicalIndex">The logical index in the circular list.</param>
        /// <returns>The corresponding physical index in the underlying array.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when logical index is out of range.
        /// </exception>
#if !NETSTANDARD2_0
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        private int GetPhysicalIndex(int logicalIndex)
        {
            if (logicalIndex < 0 || logicalIndex >= this.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalIndex),
                    logicalIndex,
                    $"Index must be between 0 and {this.Count - 1}.");
            }

            return (this.startIndex + logicalIndex) % this.Capacity;
        }
    }
}
