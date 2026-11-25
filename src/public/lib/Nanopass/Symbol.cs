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

public readonly record struct Symbol(string Name) : IComparable<Symbol>
{
    private readonly string? name = Name is "" ? null : Name;

    public string Name
    {
        get => this.name ?? string.Empty;
        init => this.name = value is "" ? null : value;
    }

    public static implicit operator Symbol(string name) => FromString(name);

    public static bool operator <(Symbol left, Symbol right) => left.CompareTo(right) < 0;

    public static bool operator <=(Symbol left, Symbol right) => left.CompareTo(right) <= 0;

    public static bool operator >(Symbol left, Symbol right) => left.CompareTo(right) > 0;

    public static bool operator >=(Symbol left, Symbol right) => left.CompareTo(right) >= 0;

    public static Symbol FromString(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name);
    }

    public int CompareTo(Symbol other) => string.CompareOrdinal(this.Name, other.Name);

    public override string ToString() => this.Name;
}
