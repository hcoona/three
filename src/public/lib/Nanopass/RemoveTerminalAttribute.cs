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
/// Removes an inherited terminal from a derived Nanopass language by CLR type or terminal name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RemoveTerminalAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTerminalAttribute"/> class matching a
    /// terminal by CLR representation type.
    /// </summary>
    /// <param name="clrType">The CLR representation type of the terminal to remove.</param>
    public RemoveTerminalAttribute(Type clrType)
    {
        this.ClrType = clrType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveTerminalAttribute"/> class matching a
    /// terminal by name.
    /// </summary>
    /// <param name="terminalName">The name of the terminal to remove.</param>
    public RemoveTerminalAttribute(string terminalName)
    {
        this.TerminalName = terminalName;
    }

    /// <summary>
    /// Gets the CLR representation type of the terminal to remove, or <see langword="null"/> when
    /// matched by name.
    /// </summary>
    public Type? ClrType { get; }

    /// <summary>
    /// Gets the name of the terminal to remove, or <see langword="null"/> when matched by CLR type.
    /// </summary>
    public string? TerminalName { get; }
}
