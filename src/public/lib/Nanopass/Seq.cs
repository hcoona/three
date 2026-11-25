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
using System.Collections.Immutable;

namespace Hcoona.Nanopass;

/// <summary>
/// Provides factory methods for <see cref="Seq{T}"/> instances and C# collection expressions.
/// </summary>
public static class Seq
{
    /// <summary>
    /// Creates an immutable value-equatable sequence copying the elements of
    /// <paramref name="items"/>.
    /// </summary>
    /// <typeparam name="T">The element type of the sequence.</typeparam>
    /// <param name="items">The elements to copy into the sequence.</param>
    /// <returns>
    /// A <see cref="Seq{T}"/> containing the elements of <paramref name="items"/>.
    /// </returns>
    public static Seq<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));
}
