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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Hcoona.Nanopass.Generators;

internal readonly record struct LocationInfo(string Path, TextSpan Span, LinePositionSpan Lines)
{
    internal static LocationInfo From(Location location) =>
        new(location.GetLineSpan().Path, location.SourceSpan, location.GetLineSpan().Span);

    internal Location ToLocation() => Location.Create(this.Path, this.Span, this.Lines);
}
