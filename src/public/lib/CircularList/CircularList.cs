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
using System.Runtime.CompilerServices;

#if PAL
using PalLang;

[assembly: TranslateAnnotatedOnly]
#endif

namespace IO.Github.Hcoona.Collections
{
#if PAL
    [Spec]
    internal struct CircularListModel<T>
    {
        public Seq<T> Items;
        public Seq<T> Store;
        public SpecInt Capacity;
        public SpecInt StartIndex;
        public SpecInt Version;
    }
#endif

    /// <summary>
    /// A fixed-capacity circular list that overwrites the oldest element when full.
    /// </summary>
    /// <typeparam name="T">The type of elements in the circular list.</typeparam>
#if PAL
    [Invariant("0 < items.Length")]
    [Invariant("0 <= count")]
    [Invariant("count <= items.Length")]
    [Invariant("0 <= startIndex")]
    [Invariant("startIndex < items.Length")]
#endif
    public class CircularList<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly T[] items;
        private int count;
        private int startIndex;
#if PAL
        [Wrapping]
#endif
        private int version;

        /// <summary>
        /// Initializes a new instance of the <see cref="CircularList{T}"/> class
        /// with the specified positive capacity.
        /// </summary>
        /// <param name="capacity">The maximum number of items the circular list can hold.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="capacity"/> is less than or equal to zero.
        /// </exception>
#if PAL
        [Throws(typeof(ArgumentOutOfRangeException), "capacity <= 0")]
        [Ensures("@this.SpecCount == 0")]
        [Ensures("@this.SpecCapacity == capacity")]
#endif
        public CircularList(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    capacity,
                    "Capacity must be positive.");
            }

            this.items = new T[capacity];
            this.count = 0;
            this.startIndex = 0;
            this.version = 0;
        }

        /// <summary>
        /// Occurs after an existing oldest item has been overwritten due to capacity overflow.
        /// </summary>
#if !NETSTANDARD2_0 && !NET462
        public event EventHandler<T>? OnOverflow;
#else
        public event EventHandler<T> OnOverflow;
#endif

#if PAL
        /// <summary>
        /// Gets the logical sequence of elements ordered from oldest to newest.
        /// </summary>
        [Spec]
        internal Seq<T> Items => this.Model.Items;

        /// <summary>
        /// Gets the raw backing array contents for representation-level contracts.
        /// </summary>
        [Spec]
        internal Seq<T> SpecStore => this.Model.Store;

        /// <summary>
        /// Gets the specification element count.
        /// </summary>
        [Spec]
        internal SpecInt SpecCount => this.Model.Items.Length;

        /// <summary>
        /// Gets the specification capacity.
        /// </summary>
        [Spec]
        internal SpecInt SpecCapacity => this.Model.Capacity;

        /// <summary>
        /// Gets the specification physical start index.
        /// </summary>
        [Spec]
        internal SpecInt SpecStartIndex => this.Model.StartIndex;

        /// <summary>
        /// Gets the specification modification counter.
        /// </summary>
        [Spec]
        internal SpecInt SpecVersion => this.Model.Version;

        [Refine("0 < Result.Capacity")]
        [Refine("Result.Store.Length == Result.Capacity")]
        [Refine("Result.Items.Length <= Result.Capacity")]
        [Refine("0 <= Result.StartIndex && Result.StartIndex < Result.Capacity")]
        [Model]
        private CircularListModel<T> Model => new CircularListModel<T>
        {
            Items = Pal.Ring(Pal.Contents(this.items), this.startIndex, this.count),
            Store = Pal.Contents(this.items),
            Capacity = this.items.Length,
            StartIndex = this.startIndex,
            Version = this.version,
        };
#endif

        /// <summary>
        /// Gets the maximum number of items this circular list can hold.
        /// </summary>
#if PAL
        [Ensures("Result == @this.SpecCapacity")]
#endif
        public int Capacity => this.items.Length;

        /// <inheritdoc cref="ICollection{T}.Count"/>
#if PAL
        [Ensures("Result == @this.SpecCount")]
#endif
        public int Count => this.count;

        /// <inheritdoc/>
#if PAL
        [Ensures("!Result")]
#endif
        public bool IsReadOnly => false;

        /// <inheritdoc cref="IList{T}.this[int]"/>
        public T this[int index]
        {
#if PAL
            [Throws(typeof(ArgumentOutOfRangeException), "index < 0 || index >= @this.SpecCount")]
            [Ensures(
                "0 <= index && index < @this.SpecCount && Pal.Same(Result, @this.Items[index])")]
#endif
            get
            {
                int physicalIndex = this.GetPhysicalIndex(index);
                return this.items[physicalIndex];
            }

#if PAL
            [Throws(typeof(ArgumentOutOfRangeException), "index < 0 || index >= @this.SpecCount")]
            [Ensures("0 <= index && index < Pal.Old(@this.SpecCount)")]
            [Ensures("@this.Items == Pal.SetAt(Pal.Old(@this.Items), index, value)")]
            [Ensures("@this.SpecCapacity == Pal.Old(@this.SpecCapacity)")]
#endif
            set
            {
                int physicalIndex = this.GetPhysicalIndex(index);
                this.items[physicalIndex] = value;
                this.version++;
            }
        }

        /// <summary>
        /// Adds an item to the circular list. When already at capacity, overwrites the oldest item
        /// and then raises <see cref="OnOverflow"/> with the overwritten item.
        /// </summary>
        /// <param name="item">The item to add to the circular list.</param>
        public void Add(T item)
        {
            if (this.AddCore(item, out T overwrittenItem))
            {
                this.OnOverflow?.Invoke(this, overwrittenItem);
            }
        }

        /// <inheritdoc/>
#if PAL
        [Ensures("Result == (Pal.IndexOf(@this.Items, item) >= 0)")]
#endif
        public bool Contains(T item)
        {
            return this.IndexOf(item) >= 0;
        }

        /// <summary>
        /// Removes the first occurrence of a specific item from the circular list.
        /// This operation is not supported for circular lists.
        /// </summary>
        /// <param name="item">The item to remove from the circular list.</param>
        /// <returns>This method always throws <see cref="NotSupportedException"/>.</returns>
        /// <exception cref="NotSupportedException">
        /// Always thrown as removal is not supported.
        /// </exception>
#if PAL
        [Throws(typeof(NotSupportedException))]
        [Ensures("false")]
#endif
        public bool Remove(T item)
        {
            throw new NotSupportedException(
                "Remove operation is not supported for circular lists.");
        }

        /// <inheritdoc/>
#if PAL
        [Ensures("@this.SpecCount == 0")]
        [Ensures("@this.SpecCapacity == Pal.Old(@this.SpecCapacity)")]
#endif
        public void Clear()
        {
            if (this.count != 0)
            {
#if !NETSTANDARD2_0 && !NET462
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    int tailLength = this.items.Length - this.startIndex;
                    if (this.count <= tailLength)
                    {
                        Array.Clear(this.items, this.startIndex, this.count);
                    }
                    else
                    {
                        Array.Clear(this.items, this.startIndex, tailLength);
                        Array.Clear(this.items, 0, this.count - tailLength);
                    }
                }
#else
                int tailLength = this.items.Length - this.startIndex;
                if (this.count <= tailLength)
                {
                    Array.Clear(this.items, this.startIndex, this.count);
                }
                else
                {
                    Array.Clear(this.items, this.startIndex, tailLength);
                    Array.Clear(this.items, 0, this.count - tailLength);
                }
#endif
                this.count = 0;
            }

            this.startIndex = 0;
            this.version++;
        }

        /// <summary>
        /// Returns a contiguous read-only span over the circular list contents in logical order.
        /// </summary>
        /// <remarks>
        /// When the active elements are contiguous in the backing array, this method returns a
        /// zero-allocation view over internal storage that remains valid only until the next
        /// mutating operation. When the active elements wrap around the end of the backing array,
        /// this method allocates and returns a contiguous snapshot copy. Use
        /// <see cref="GetSegments"/> or <see cref="CopyTo(Span{T})"/> when zero-allocation access
        /// is required in both states.
        /// </remarks>
        /// <returns>A contiguous read-only span representing the current contents.</returns>
        public ReadOnlySpan<T> AsSpan()
        {
            if (this.count == 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            int tailLength = this.items.Length - this.startIndex;
            if (this.count <= tailLength)
            {
                return new ReadOnlySpan<T>(this.items, this.startIndex, this.count);
            }

            var result = new T[this.count];
            Array.Copy(this.items, this.startIndex, result, 0, tailLength);
            Array.Copy(this.items, 0, result, tailLength, this.count - tailLength);
            return new ReadOnlySpan<T>(result);
        }

        /// <summary>
        /// Returns up to two contiguous read-only span segments representing the circular list
        /// contents in logical order without allocating.
        /// </summary>
        /// <remarks>
        /// The returned spans alias the internal backing storage and must not be used across
        /// mutating operations on the list.
        /// </remarks>
        /// <returns>The first and optional second contiguous span segments.</returns>
        public Segments GetSegments()
        {
            if (this.count == 0)
            {
                return new Segments(ReadOnlySpan<T>.Empty, ReadOnlySpan<T>.Empty);
            }

            int tailLength = this.items.Length - this.startIndex;
            if (this.count <= tailLength)
            {
                return new Segments(
                    new ReadOnlySpan<T>(this.items, this.startIndex, this.count),
                    ReadOnlySpan<T>.Empty);
            }

            return new Segments(
                new ReadOnlySpan<T>(this.items, this.startIndex, tailLength),
                new ReadOnlySpan<T>(this.items, 0, this.count - tailLength));
        }

        /// <inheritdoc/>
#if PAL
        [Throws(typeof(ArgumentOutOfRangeException), "arrayIndex < 0")]
        [Throws(
            typeof(ArgumentException),
            "arrayIndex >= 0 && (arrayIndex > Pal.Contents(array).Length || " +
            "Pal.Contents(array).Length - arrayIndex < @this.SpecCount)")]
        [Ensures("@this.Items == Pal.Old(@this.Items)")]
        [Ensures("0 <= arrayIndex && arrayIndex + @this.SpecCount <= Pal.Contents(array).Length")]
        [Ensures(
            "Pal.Contents(array) == Pal.Splice(" +
            "Pal.Old(Pal.Contents(array)), arrayIndex, arrayIndex + @this.SpecCount, @this.Items)")]
#endif
        public void CopyTo(T[] array, int arrayIndex)
        {
#if NET6_0_OR_GREATER || PAL
            ArgumentNullException.ThrowIfNull(array);
#else
            if (array is null)
            {
                throw new ArgumentNullException(nameof(array));
            }
#endif

            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arrayIndex),
                    arrayIndex,
                    "Array index must be non-negative.");
            }

            if (arrayIndex > array.Length || array.Length - arrayIndex < this.count)
            {
                throw new ArgumentException(
                    "Destination array is not large enough to copy all items.");
            }

            if (this.count == 0)
            {
                return;
            }

            int tailLength = this.items.Length - this.startIndex;
            if (this.count <= tailLength)
            {
                Array.Copy(this.items, this.startIndex, array, arrayIndex, this.count);
            }
            else
            {
                Array.Copy(this.items, this.startIndex, array, arrayIndex, tailLength);
                Array.Copy(this.items, 0, array, arrayIndex + tailLength, this.count - tailLength);
            }
        }

        /// <summary>
        /// Copies the elements of the circular list into the destination span in logical order.
        /// </summary>
        /// <param name="destination">The destination span receiving the copied elements.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="destination"/> is shorter than <see cref="Count"/>.
        /// </exception>
        public void CopyTo(Span<T> destination)
        {
            if (destination.Length < this.count)
            {
                throw new ArgumentException(
                    "Destination span is not large enough to copy all items.",
                    nameof(destination));
            }

            Segments segments = this.GetSegments();
            segments.First.CopyTo(destination);
            if (!segments.Second.IsEmpty)
            {
                segments.Second.CopyTo(destination.Slice(segments.First.Length));
            }
        }

        /// <inheritdoc/>
#if PAL
        [Ensures("(Result >= 0) == (Pal.IndexOf(@this.Items, item) >= 0)")]
        [Ensures("Pal.Implies(Result < 0, Result == -1)")]
        [Ensures(
            "Pal.Implies(Result >= 0, 0 <= Result && Result < @this.SpecCount && " +
            "Pal.DefaultEquals(@this.Items[Result], item))")]
#endif
        public int IndexOf(T item)
        {
            if (this.count == 0)
            {
                return -1;
            }

            int tailLength = this.items.Length - this.startIndex;
            if (this.count <= tailLength)
            {
                int index = Array.IndexOf(this.items, item, this.startIndex, this.count);
                return index >= 0 ? index - this.startIndex : -1;
            }

            int firstIndex = Array.IndexOf(this.items, item, this.startIndex, tailLength);
            if (firstIndex >= 0)
            {
                return firstIndex - this.startIndex;
            }

            int secondIndex = Array.IndexOf(this.items, item, 0, this.count - tailLength);
            return secondIndex >= 0 ? tailLength + secondIndex : -1;
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
#if PAL
        [Throws(typeof(NotSupportedException))]
        [Ensures("false")]
#endif
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
#if PAL
        [Throws(typeof(NotSupportedException))]
        [Ensures("false")]
#endif
        public void RemoveAt(int index)
        {
            throw new NotSupportedException(
                "RemoveAt operation is not supported for circular lists. " +
                "Use Clear() to reset the list.");
        }

        /// <summary>
        /// Returns a value-type enumerator that iterates through the circular list in logical
        /// order.
        /// </summary>
        /// <returns>An <see cref="Enumerator"/> for the circular list.</returns>
#if PAL
        [Ensures(
            "ReferenceEquals(Result.list, @this) && " +
            "Result.version == @this.SpecVersion && Result.index == 0")]
#endif
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <inheritdoc/>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return new Enumerator(this);
        }

#if PAL
        [Ensures("Result == (Pal.Old(@this.SpecCount) == Pal.Old(@this.SpecCapacity))")]
        [Ensures("@this.SpecCapacity == Pal.Old(@this.SpecCapacity)")]
        [Ensures("Pal.Implies(!Result, @this.Items == Pal.Snoc(Pal.Old(@this.Items), item))")]
        [Ensures(
            "Pal.Implies(Result, Pal.Same(overwrittenItem, Pal.Old(@this.Items)[0]) && " +
            "@this.Items == Pal.Snoc(" +
            "Pal.Slice(Pal.Old(@this.Items), 1, Pal.Old(@this.SpecCount)), item))")]
#endif
        private bool AddCore(T item, out T overwrittenItem)
        {
            if (this.count == this.items.Length)
            {
                int head = this.startIndex;
                overwrittenItem = this.items[head];
                this.items[head] = item;

                int nextStart = head + 1;
                if (nextStart == this.items.Length)
                {
                    nextStart = 0;
                }

                this.startIndex = nextStart;
                this.version++;
                return true;
            }

            int tailLength = this.items.Length - this.startIndex;
            int writeIndex = this.count < tailLength
                ? this.startIndex + this.count
                : this.count - tailLength;
            this.items[writeIndex] = item;
            this.count++;
            this.version++;
#if NETSTANDARD2_0 || NET462
            overwrittenItem = default(T);
#else
            overwrittenItem = default!;
#endif
            return false;
        }

        /// <summary>
        /// Converts a logical index to a physical backing-array index without signed overflow.
        /// </summary>
        /// <param name="logicalIndex">The logical index in the circular list.</param>
        /// <returns>The corresponding physical index in the underlying array.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="logicalIndex"/> is outside <c>[0, Count)</c>.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if PAL
        [Throws(
            typeof(ArgumentOutOfRangeException),
            "logicalIndex < 0 || logicalIndex >= @this.SpecCount")]
        [Ensures("0 <= logicalIndex && logicalIndex < @this.SpecCount")]
        [Ensures("0 <= Result && Result < @this.SpecCapacity")]
        [Ensures(
            "Result == (logicalIndex < @this.SpecCapacity - @this.SpecStartIndex " +
            "? @this.SpecStartIndex + logicalIndex " +
            ": logicalIndex - (@this.SpecCapacity - @this.SpecStartIndex))")]
        [Ensures("Pal.Same(@this.SpecStore[Result], @this.Items[logicalIndex])")]
#endif
        private int GetPhysicalIndex(int logicalIndex)
        {
            if (logicalIndex < 0 || logicalIndex >= this.count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalIndex),
                    logicalIndex,
                    "Index must be non-negative and less than Count.");
            }

            int tailLength = this.items.Length - this.startIndex;
            return logicalIndex < tailLength
                ? this.startIndex + logicalIndex
                : logicalIndex - tailLength;
        }

        /// <summary>
        /// Represents up to two contiguous read-only span segments over a
        /// <see cref="CircularList{T}"/> without allocating.
        /// </summary>
        public readonly ref struct Segments
        {
            internal Segments(ReadOnlySpan<T> first, ReadOnlySpan<T> second)
            {
                this.First = first;
                this.Second = second;
            }

            /// <summary>
            /// Gets the first contiguous segment (empty only when the list is empty).
            /// </summary>
            public ReadOnlySpan<T> First { get; }

            /// <summary>
            /// Gets the second contiguous segment (non-empty only when the buffer wraps around).
            /// </summary>
            public ReadOnlySpan<T> Second { get; }

            /// <summary>
            /// Gets the total number of elements across both segments.
            /// </summary>
            public int Count => this.First.Length + this.Second.Length;

            /// <summary>
            /// Gets a value indicating whether both segments are empty.
            /// </summary>
            public bool IsEmpty => this.First.IsEmpty;

            /// <summary>
            /// Deconstructs the segment pair into its first and second contiguous spans.
            /// </summary>
            /// <param name="first">Receives <see cref="First"/>.</param>
            /// <param name="second">Receives <see cref="Second"/>.</param>
            public void Deconstruct(out ReadOnlySpan<T> first, out ReadOnlySpan<T> second)
            {
                first = this.First;
                second = this.Second;
            }
        }

        /// <summary>
        /// Enumerates the elements of a <see cref="CircularList{T}"/> in logical order.
        /// </summary>
        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            private readonly CircularList<T> list;
            private readonly int version;
            private int index;
            private T current;

#if PAL
            [Ensures(
                "ReferenceEquals(@this.list, list) && " +
                "@this.version == list.SpecVersion && @this.index == 0")]
#endif
            internal Enumerator(CircularList<T> list)
            {
                this.list = list;
                this.version = list.version;
                this.index = 0;
#if NETSTANDARD2_0 || NET462
                this.current = default(T);
#else
                this.current = default!;
#endif
            }

            /// <inheritdoc/>
#if PAL
            [Ensures("Pal.Same(Result, @this.current)")]
#endif
            public T Current => this.current;

            /// <inheritdoc/>
#if !NETSTANDARD2_0 && !NET462
            object? IEnumerator.Current
#else
            object IEnumerator.Current
#endif
            {
                get
                {
                    if (this.index <= 0)
                    {
                        throw new InvalidOperationException(
                            "Enumeration has either not started or has already finished.");
                    }

                    return this.Current;
                }
            }

            /// <inheritdoc/>
#if PAL
            [Ensures("@this.index == -1")]
#endif
            public void Dispose()
            {
                this.index = -1;
#if NETSTANDARD2_0 || NET462
                this.current = default(T);
#else
                this.current = default!;
#endif
            }

            /// <inheritdoc/>
#if PAL
            [Throws(typeof(InvalidOperationException), "@this.version != @this.list.SpecVersion")]
            [Ensures("@this.version == @this.list.SpecVersion")]
            [Ensures(
                "Result == (0 <= Pal.Old(@this.index) && " +
                "Pal.Old(@this.index) < @this.list.SpecCount)")]
            [Ensures(
                "Pal.Implies(Result, @this.index == Pal.Old(@this.index) + 1 && " +
                "Pal.Same(@this.current, @this.list.Items[Pal.Old(@this.index)]))")]
            [Ensures("Pal.Implies(!Result, @this.index == -1)")]
#endif
            public bool MoveNext()
            {
                CircularList<T> localList = this.list;
                if (this.version != localList.version)
                {
                    throw new InvalidOperationException(
                        "Collection was modified; enumeration operation may not execute.");
                }

                if (this.index >= 0 && this.index < localList.count)
                {
                    int tailLength = localList.items.Length - localList.startIndex;
                    int physicalIndex = this.index < tailLength
                        ? localList.startIndex + this.index
                        : this.index - tailLength;
                    this.current = localList.items[physicalIndex];
                    this.index++;
                    return true;
                }

                this.index = -1;
#if NETSTANDARD2_0 || NET462
                this.current = default(T);
#else
                this.current = default!;
#endif
                return false;
            }

            /// <inheritdoc/>
#if PAL
            [Throws(typeof(InvalidOperationException), "@this.version != @this.list.SpecVersion")]
            [Ensures("@this.version == @this.list.SpecVersion && @this.index == 0")]
#endif
            void IEnumerator.Reset()
            {
                if (this.version != this.list.version)
                {
                    throw new InvalidOperationException(
                        "Collection was modified; enumeration operation may not execute.");
                }

                this.index = 0;
#if NETSTANDARD2_0 || NET462
                this.current = default(T);
#else
                this.current = default!;
#endif
            }
        }
    }
}
