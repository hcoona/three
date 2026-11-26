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
/// Marks a synthesized or custom processor method on a pass.
/// </summary>
/// <remarks>
/// Unbound optional parameters use their defaults, and unbound params parameters are empty.
/// </remarks>
/// <param name="from">The input grammar kind, or null to infer it from the signature.</param>
/// <param name="to">The output grammar kind, or null to infer it from the signature.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ProcessorAttribute(string? from = null, string? to = null) : Attribute
{
    /// <summary>
    /// Gets the declared input grammar kind.
    /// </summary>
    public string? From { get; } = from;

    /// <summary>
    /// Gets the declared output grammar kind.
    /// </summary>
    public string? To { get; } = to;

    /// <summary>
    /// Gets or initializes the extra return values used by synthesized clauses.
    /// </summary>
    public object?[]? DefaultExtraReturns { get; init; }
}
