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

namespace Hcoona.Nanopass;

/// <summary>
/// Represents an S-expression symbol compared by ordinal character value.
/// </summary>
/// <param name="Name">The textual name of the symbol.</param>
public readonly record struct Symbol(string Name) : IComparable<Symbol>
{
    private readonly string? name = Name is "" ? null : Name;

    /// <summary>
    /// Gets or initializes the textual name of the symbol, defaulting to
    /// <see cref="string.Empty"/>.
    /// </summary>
    public string Name
    {
        get => this.name ?? string.Empty;
        init => this.name = value is "" ? null : value;
    }

    /// <summary>
    /// Converts a string into a <see cref="Symbol"/>.
    /// </summary>
    /// <param name="name">The textual name of the symbol.</param>
    /// <returns>A <see cref="Symbol"/> with the specified <paramref name="name"/>.</returns>
    public static implicit operator Symbol(string name) => FromString(name);

    /// <summary>
    /// Determines whether one symbol precedes another in ordinal sort order.
    /// </summary>
    /// <param name="left">The first symbol to compare.</param>
    /// <param name="right">The second symbol to compare.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> precedes <paramref name="right"/>;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator <(Symbol left, Symbol right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one symbol precedes or equals another in ordinal sort order.
    /// </summary>
    /// <param name="left">The first symbol to compare.</param>
    /// <param name="right">The second symbol to compare.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> precedes or equals
    /// <paramref name="right"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator <=(Symbol left, Symbol right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one symbol follows another in ordinal sort order.
    /// </summary>
    /// <param name="left">The first symbol to compare.</param>
    /// <param name="right">The second symbol to compare.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> follows <paramref name="right"/>;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator >(Symbol left, Symbol right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one symbol follows or equals another in ordinal sort order.
    /// </summary>
    /// <param name="left">The first symbol to compare.</param>
    /// <param name="right">The second symbol to compare.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="left"/> follows or equals
    /// <paramref name="right"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator >=(Symbol left, Symbol right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Creates a <see cref="Symbol"/> from a non-null string.
    /// </summary>
    /// <param name="name">The textual name of the symbol.</param>
    /// <returns>A <see cref="Symbol"/> with the specified <paramref name="name"/>.</returns>
    public static Symbol FromString(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name);
    }

    /// <summary>
    /// Compares this symbol with another symbol using ordinal string comparison.
    /// </summary>
    /// <param name="other">The symbol to compare with this instance.</param>
    /// <returns>
    /// A signed integer indicating the relative ordinal order of this symbol and
    /// <paramref name="other"/>.
    /// </returns>
    public int CompareTo(Symbol other) => string.CompareOrdinal(this.Name, other.Name);

    /// <summary>
    /// Returns the unescaped textual name of the symbol.
    /// </summary>
    /// <returns>The <see cref="Name"/> of the symbol.</returns>
    public override string ToString() => this.Name;
}
