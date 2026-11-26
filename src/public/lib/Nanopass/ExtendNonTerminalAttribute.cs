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
/// Extends, adds, or removes productions on a nonterminal in a derived Nanopass language.
/// </summary>
/// <param name="name">The name of the nonterminal to declare or modify.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ExtendNonTerminalAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the name of the nonterminal to declare or modify.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Gets or initializes the metavariable prefixes introduced for the nonterminal.
    /// </summary>
    public string[]? MetaVars { get; init; }

    /// <summary>
    /// Gets or initializes the S-expression production patterns to remove from the nonterminal.
    /// </summary>
    public string[]? Remove { get; init; }

    /// <summary>
    /// Gets or initializes the S-expression production patterns to add to the nonterminal.
    /// </summary>
    public string[]? Add { get; init; }
}
