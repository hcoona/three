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
/// Declares a Nanopass nonterminal, its metavariable prefixes, and its S-expression productions on
/// a language class.
/// </summary>
/// <param name="name">The name of the nonterminal.</param>
/// <param name="metaVars">
/// The metavariable prefixes bound to the nonterminal in grammar patterns.
/// </param>
/// <param name="productions">
/// The S-expression production clauses belonging to the nonterminal.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class NonTerminalDefAttribute(
    string name,
    string[] metaVars,
    params string[] productions) : Attribute
{
    /// <summary>
    /// Gets the name of the nonterminal.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets the metavariable prefixes bound to the nonterminal in grammar patterns.
    /// </summary>
    public string[] MetaVars { get; } = metaVars;

    /// <summary>
    /// Gets the S-expression production clauses belonging to the nonterminal.
    /// </summary>
    public string[] Productions { get; } = productions;
}
