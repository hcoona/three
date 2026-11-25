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
/// Describes a terminal and its metavariable bindings within a <see cref="LanguageDescriptor"/>.
/// </summary>
/// <param name="Name">The symbol naming the terminal.</param>
/// <param name="MetaVariables">The metavariable prefixes associated with the terminal.</param>
/// <param name="ClrType">The CLR representation type of the terminal, if known.</param>
/// <param name="Predicate">The optional predicate method name validating terminal values.</param>
/// <param name="Prettifier">
/// The optional S-expression prettifier descriptor of the terminal.
/// </param>
public sealed record TerminalDescriptor(
    Symbol Name,
    Seq<Symbol> MetaVariables,
    Type? ClrType = null,
    string? Predicate = null,
    SExpr? Prettifier = null);
