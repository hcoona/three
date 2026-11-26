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
/// Marks a method as a rewrite clause for a pass processor.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RewriteAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RewriteAttribute"/> class
    /// without an explicit production type.
    /// </summary>
    public RewriteAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RewriteAttribute"/> class
    /// for the specified production type.
    /// </summary>
    /// <param name="productionType">The input production type recognized by the clause.</param>
    public RewriteAttribute(Type productionType)
    {
        this.ProductionType = productionType;
    }

    /// <summary>
    /// Gets the explicit production type, if one was specified.
    /// </summary>
    public Type? ProductionType { get; }

    /// <summary>
    /// Gets or initializes the name of the processor that owns this clause.
    /// </summary>
    public string? Processor { get; init; }

    /// <summary>
    /// Gets or initializes the name of a method that tests whether this clause applies.
    /// </summary>
    public string? Guard { get; init; }

    /// <summary>
    /// Gets or initializes the order in which the clause is tested against its peers.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether this is the fallback clause.
    /// </summary>
    public bool IsElse { get; init; }
}
