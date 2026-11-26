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
/// Customizes pretty-printed S-expression unparsing for a Nanopass production record.
/// </summary>
/// <param name="template">
/// An optional S-expression prettifier template rendered instead of the raw syntax pattern.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PrettyAttribute(string? template = null) : Attribute
{
    /// <summary>
    /// Gets the S-expression prettifier template, or <see langword="null"/> when a handler is used.
    /// </summary>
    public string? Template { get; } = template;

    /// <summary>
    /// Gets or sets the static handler that transforms the production's S-expression.
    /// The handler takes one <see cref="SExpr"/> with recursively prettified fields.
    /// </summary>
    public string? PrettyHandler { get; set; }
}
