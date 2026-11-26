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
using System.Linq;

namespace Hcoona.Nanopass.Generators;

internal sealed record PassType(
    string Name,
    EquatableArray<PassType> Arguments = default,
    EquatableArray<string> TupleNames = default,
    bool Nullable = false,
    int ArrayRank = 0,
    PassType? ContainingType = null,
    EquatableArray<string> Candidates = default)
{
    private int nodeCount;
    private string? key;
    private string? code;

    private PassType(PassType original)
    {
        this.Name = original.Name;
        this.Arguments = original.Arguments;
        this.TupleNames = original.TupleNames;
        this.Nullable = original.Nullable;
        this.ArrayRank = original.ArrayRank;
        this.ContainingType = original.ContainingType;
        this.Candidates = original.Candidates;
    }

    internal bool IsTuple => this.HasTupleElements && !this.Nullable;

    internal bool HasTupleElements => this.TupleNames.Count > 0;

    internal PassType Primary => this.IsTuple ? this.Arguments[0] : this;

    internal int NodeCount => this.nodeCount != 0
        ? this.nodeCount
        : (this.nodeCount = this.ComputeNodeCount());

    internal string Code => this.code ??= this.Format(includeTupleNames: true);

    internal string Key => this.key ??= this.Format(includeTupleNames: false);

    public bool Equals(PassType? other) =>
        other is not null
        && string.Equals(this.Name, other.Name, StringComparison.Ordinal)
        && this.Arguments.Equals(other.Arguments)
        && this.TupleNames.Equals(other.TupleNames)
        && this.Nullable == other.Nullable
        && this.ArrayRank == other.ArrayRank
        && Equals(this.ContainingType, other.ContainingType)
        && this.Candidates.Equals(other.Candidates);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(this.Name);
            hash = (hash * 397) ^ this.Arguments.GetHashCode();
            hash = (hash * 397) ^ this.TupleNames.GetHashCode();
            hash = (hash * 397) ^ (this.Nullable ? 1 : 0);
            hash = (hash * 397) ^ this.ArrayRank;
            hash = (hash * 397) ^ (this.ContainingType?.GetHashCode() ?? 0);
            return (hash * 397) ^ this.Candidates.GetHashCode();
        }
    }

    private string Format(bool includeTupleNames)
    {
        string Render(PassType element) => includeTupleNames ? element.Code : element.Key;

        if (this.ArrayRank > 0)
        {
            var element = this;
            string ranks = string.Empty;
            do
            {
                ranks += "[" + new string(',', element.ArrayRank - 1) + "]";
                element = element.Arguments[0];
            }
            while (element.ArrayRank > 0 && !element.Nullable);

            return Render(element) + ranks + (this.Nullable ? "?" : string.Empty);
        }

        string text = this.ContainingType is { } owner
            ? Render(owner) + "." + this.Name
            : this.Name;
        if (this.HasTupleElements)
        {
            text = "(" + string.Join(", ", this.Arguments.Select((element, index) =>
                Render(element) + (includeTupleNames && this.TupleNames[index].Length > 0
                    ? " " + LanguageEmitter.Id(this.TupleNames[index])
                    : string.Empty))) + ")";
        }
        else if (this.Arguments.Count > 0)
        {
            text += "<" + string.Join(", ", this.Arguments.Select(Render)) + ">";
        }

        return text + (this.Nullable ? "?" : string.Empty);
    }

    private int ComputeNodeCount()
    {
        int total = 1 + (this.ContainingType?.NodeCount ?? 0);
        foreach (PassType argument in this.Arguments)
        {
            total += argument.NodeCount;
            if (total > 1025)
            {
                return 1025;
            }
        }

        return total;
    }
}
