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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Hcoona.Nanopass;

/// <summary>
/// Represents an immutable sequence with structural element-wise equality and collection-expression
/// support.
/// </summary>
/// <typeparam name="T">The type of elements stored in the sequence.</typeparam>
[CollectionBuilder(typeof(Seq), nameof(Seq.Create))]
public readonly struct Seq<T> : IReadOnlyList<T>, IEquatable<Seq<T>>
{
    private readonly ImmutableArray<T> items;

    internal Seq(ImmutableArray<T> items)
    {
        this.items = items;
    }

    /// <summary>
    /// Gets an empty <see cref="Seq{T}"/> instance.
    /// </summary>
    [SuppressMessage(
        "Design", "CA1000", Justification = "Matches the pattern of ImmutableArray<T>.Empty.")]
    public static Seq<T> Empty => default;

    /// <summary>
    /// Gets the number of elements in the sequence.
    /// </summary>
    public int Length => this.items.IsDefault ? 0 : this.items.Length;

    /// <summary>
    /// Gets the number of elements in the sequence.
    /// </summary>
    public int Count => this.Length;

    /// <summary>
    /// Gets a value indicating whether the sequence contains no elements.
    /// </summary>
    public bool IsEmpty => this.items.IsDefaultOrEmpty;

    private ImmutableArray<T> Items => this.items.IsDefault ? ImmutableArray<T>.Empty : this.items;

    /// <summary>
    /// Gets the element at the specified zero-based index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at <paramref name="index"/>.</returns>
    public T this[int index] => this.Items[index];

    /// <summary>
    /// Determines whether two sequences contain equal elements in the same order.
    /// </summary>
    /// <param name="left">The first sequence to compare.</param>
    /// <param name="right">The second sequence to compare.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> and <paramref name="right"/> are
    /// element-wise equal; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(Seq<T> left, Seq<T> right) => left.Equals(right);

    /// <summary>
    /// Determines whether two sequences do not contain equal elements in the same order.
    /// </summary>
    /// <param name="left">The first sequence to compare.</param>
    /// <param name="right">The second sequence to compare.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> and <paramref name="right"/> differ;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(Seq<T> left, Seq<T> right) => !left.Equals(right);

    /// <summary>
    /// Forms a slice out of the current sequence starting at a specified index for a specified
    /// length.
    /// </summary>
    /// <param name="start">The zero-based index at which to begin the slice.</param>
    /// <param name="length">The number of elements to include in the slice.</param>
    /// <returns>A <see cref="Seq{T}"/> containing the sliced elements.</returns>
    public Seq<T> Slice(int start, int length) =>
        new(ImmutableArray.Create(this.Items, start, length));

    /// <summary>
    /// Returns a read-only span over the sequence elements.
    /// </summary>
    /// <returns>A <see cref="ReadOnlySpan{T}"/> viewing the sequence elements.</returns>
    public ReadOnlySpan<T> AsSpan() => this.items.AsSpan();

    /// <summary>
    /// Projects each element of the sequence into a new form.
    /// </summary>
    /// <typeparam name="TResult">The element type of the resulting sequence.</typeparam>
    /// <param name="selector">A transform function to apply to each element.</param>
    /// <returns>A <see cref="Seq{T}"/> containing the projected elements.</returns>
    public Seq<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new(ImmutableArray.CreateRange(this.Items, selector));
    }

    /// <summary>
    /// Projects each element of the sequence into a new form using an explicit state argument to
    /// avoid closure allocations.
    /// </summary>
    /// <typeparam name="TState">The type of the caller-supplied state value.</typeparam>
    /// <typeparam name="TResult">The element type of the resulting sequence.</typeparam>
    /// <param name="state">
    /// The state value forwarded to each invocation of <paramref name="selector"/>.
    /// </param>
    /// <param name="selector">
    /// A transform function applied to each element and <paramref name="state"/>.
    /// </param>
    /// <returns>A <see cref="Seq{T}"/> containing the projected elements in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is null.</exception>
    public Seq<TResult> Select<TState, TResult>(TState state, Func<T, TState, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (this.IsEmpty)
        {
            return Seq<TResult>.Empty;
        }

        var result = new TResult[this.items.Length];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = selector(this.items[i], state);
        }

        return new(ImmutableCollectionsMarshal.AsImmutableArray(result));
    }

    /// <summary>
    /// Determines whether the current sequence is element-wise equal to another sequence.
    /// </summary>
    /// <param name="other">The sequence to compare with this instance.</param>
    /// <returns>
    /// <see langword="true"/> if the sequences have equal length and equal elements in order;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(Seq<T> other) => this.AsSpan().SequenceEqual(
        other.AsSpan(),
        EqualityComparer<T>.Default);

    /// <summary>
    /// Determines whether the specified object is a <see cref="Seq{T}"/> equal to this instance.
    /// </summary>
    /// <param name="obj">The object to compare with this instance.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="obj"/> is an equal <see cref="Seq{T}"/>;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj) => obj is Seq<T> other && this.Equals(other);

    /// <summary>
    /// Computes a structural hash code from the elements in the sequence.
    /// </summary>
    /// <returns>A hash code for the current sequence.</returns>
    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (T item in this)
        {
            hash.Add(item, EqualityComparer<T>.Default);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Returns a value-type enumerator that iterates through the sequence.
    /// </summary>
    /// <returns>An enumerator for the sequence.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator() => this.Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)this.Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
}
