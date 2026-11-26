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
/// Declares a terminal type, its metavariable prefixes, and optional predicate or prettifier hooks
/// on a Nanopass language class.
/// </summary>
/// <param name="clrType">The CLR representation type of the terminal.</param>
/// <param name="metaVars">
/// The metavariable prefixes bound to the terminal in grammar patterns.
/// </param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TerminalAttribute(Type clrType, params string[] metaVars) : Attribute
{
    /// <summary>
    /// Gets the CLR representation type of the terminal.
    /// </summary>
    public Type ClrType { get; } = clrType;

    /// <summary>
    /// Gets the metavariable prefixes bound to the terminal in grammar patterns.
    /// </summary>
    public string[] MetaVars { get; } = metaVars;

    /// <summary>
    /// Gets or sets the grammar name of the terminal, defaulting to the lowercase CLR type name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the name of a static predicate method on the language class that validates
    /// terminal values.
    /// </summary>
    public string? Predicate { get; set; }

    /// <summary>
    /// Gets or sets the name of a static prettifier method on the language class that converts a
    /// terminal value to an <see cref="SExpr"/>.
    /// </summary>
    public string? Prettifier { get; set; }
}
