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
/// Declares a Nanopass transformation and its language endpoints.
/// </summary>
/// <param name="inputLanguage">The input language type, or <see cref="RawTerm"/>.</param>
/// <param name="outputLanguage">The output language type, or <see cref="RawTerm"/>.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PassAttribute(Type inputLanguage, Type outputLanguage) : Attribute
{
    /// <summary>
    /// Gets the input language type, or <see cref="RawTerm"/> for a raw input.
    /// </summary>
    public Type InputLanguage { get; } = inputLanguage;

    /// <summary>
    /// Gets the output language type, or <see cref="RawTerm"/> for a raw output.
    /// </summary>
    public Type OutputLanguage { get; } = outputLanguage;

    /// <summary>
    /// Gets or initializes the input entry nonterminal, overriding the language entry.
    /// </summary>
    public string? InputEntry { get; init; }

    /// <summary>
    /// Gets or initializes the output entry nonterminal, overriding the language entry.
    /// </summary>
    public string? OutputEntry { get; init; }

    /// <summary>
    /// Gets or initializes a value indicating whether the pass emits trace events.
    /// </summary>
    public bool Trace { get; init; }
}
