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

namespace Hcoona.Nanopass;

[CollectionBuilder(typeof(Seq), nameof(Seq.Create))]
public readonly struct Seq<T> : IReadOnlyList<T>, IEquatable<Seq<T>>
{
    private readonly ImmutableArray<T> items;

    internal Seq(ImmutableArray<T> items)
    {
        this.items = items;
    }

    [SuppressMessage(
        "Design", "CA1000", Justification = "Matches the pattern of ImmutableArray<T>.Empty.")]
    public static Seq<T> Empty => default;

    public int Length => this.items.IsDefault ? 0 : this.items.Length;

    public int Count => this.Length;

    public bool IsEmpty => this.items.IsDefaultOrEmpty;

    private ImmutableArray<T> Items => this.items.IsDefault ? ImmutableArray<T>.Empty : this.items;

    public T this[int index] => this.Items[index];

    public static bool operator ==(Seq<T> left, Seq<T> right) => left.Equals(right);

    public static bool operator !=(Seq<T> left, Seq<T> right) => !left.Equals(right);

    public Seq<T> Slice(int start, int length) =>
        new(ImmutableArray.Create(this.Items, start, length));

    public ReadOnlySpan<T> AsSpan() => this.items.AsSpan();

    public Seq<TResult> Select<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new(ImmutableArray.CreateRange(this.Items, selector));
    }

    public bool Equals(Seq<T> other) => this.AsSpan().SequenceEqual(
        other.AsSpan(),
        EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => obj is Seq<T> other && this.Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (T item in this)
        {
            hash.Add(item, EqualityComparer<T>.Default);
        }

        return hash.ToHashCode();
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => this.Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)this.Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
}
