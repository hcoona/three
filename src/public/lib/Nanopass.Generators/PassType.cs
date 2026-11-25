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

using System.Linq;

namespace Hcoona.Nanopass.Generators;

internal sealed record PassType(
    string Name,
    EquatableArray<PassType> Arguments = default,
    EquatableArray<string> TupleNames = default,
    bool Nullable = false,
    int ArrayRank = 0)
{
    internal bool IsTuple => this.TupleNames.Count > 0;

    internal PassType Primary => this.IsTuple ? this.Arguments[0] : this;

    internal string Code => this.Format(includeTupleNames: true);

    internal string Key => this.Format(includeTupleNames: false);

    private string Format(bool includeTupleNames)
    {
        string Render(PassType element) => includeTupleNames ? element.Code : element.Key;

        string text = this.ArrayRank > 0
            ? Render(this.Arguments[0]) + "[" + new string(',', this.ArrayRank - 1) + "]"
            : this.Name;
        if (this.IsTuple)
        {
            text = "(" + string.Join(", ", this.Arguments.Select((element, index) =>
                Render(element) + (includeTupleNames && this.TupleNames[index].Length > 0
                    ? " " + LanguageEmitter.Id(this.TupleNames[index])
                    : string.Empty))) + ")";
        }
        else if (this.ArrayRank == 0 && this.Arguments.Count > 0)
        {
            text += "<" + string.Join(", ", this.Arguments.Select(Render)) + ">";
        }

        return text + (this.Nullable ? "?" : string.Empty);
    }
}
