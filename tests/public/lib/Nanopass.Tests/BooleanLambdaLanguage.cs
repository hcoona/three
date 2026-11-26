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

namespace Hcoona.Nanopass.Tests;

[Language]
[Terminal(typeof(bool), "b", Name = "boolean")]
[Terminal(typeof(Symbol), "x", Name = "variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(Symbol), "p", Name = "primitive", Predicate = nameof(IsPrimitive))]
[NonTerminalDef(
    "Expr",
    ["e"],
    "v",
    "x",
    "p",
    "(lambda (x) e)",
    "(and e0 e1)",
    "(or e0 e1)",
    "(not e)",
    "(e0 e1)")]
[NonTerminalDef("Value", ["v"], "b")]
public static partial class BooleanLambdaLanguage
{
    private static bool IsPrimitive(Symbol symbol) =>
        string.Equals(symbol.Name, "+", StringComparison.Ordinal);

    private static bool IsVariable(Symbol symbol) => !IsPrimitive(symbol);
}
