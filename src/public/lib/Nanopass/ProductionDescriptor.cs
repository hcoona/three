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

namespace Hcoona.Nanopass;

/// <summary>
/// Describes a single grammar production belonging to a <see cref="NonTerminalDescriptor"/>.
/// </summary>
/// <param name="Syntax">The raw S-expression pattern of the production.</param>
/// <param name="Name">The optional generated or explicit CLR record name of the production.</param>
/// <param name="Pretty">
/// The optional pretty-printing S-expression template of the production.
/// </param>
public sealed record ProductionDescriptor(SExpr Syntax, string? Name = null, SExpr? Pretty = null)
{
    /// <summary>
    /// Gets or initializes the terminal names referenced directly by the production, when
    /// precomputed.
    /// </summary>
    public Seq<Symbol>? ReferencedTerminals { get; init; }

    /// <summary>
    /// Gets or initializes the nonterminal names referenced directly by the production, when
    /// precomputed.
    /// </summary>
    public Seq<Symbol>? ReferencedNonTerminals { get; init; }
}
