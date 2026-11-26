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
using System.Linq;

namespace Hcoona.Nanopass.Generators;

internal readonly struct EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
{
    private readonly ImmutableArray<T> items;

    internal EquatableArray(IEnumerable<T> items)
    {
        this.items = [.. items];
    }

    public int Count => this.Items.Length;

    private ImmutableArray<T> Items => this.items.IsDefault ? ImmutableArray<T>.Empty : this.items;

    public T this[int index] => this.Items[index];

    public bool Equals(EquatableArray<T> other) => this.Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) =>
        obj is EquatableArray<T> other && this.Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in this.Items)
        {
            int itemHash = item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);
            hash = unchecked((hash * 31) + itemHash);
        }

        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)this.Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
