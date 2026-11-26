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
/// Describes the resolved grammar of a Nanopass language.
/// </summary>
/// <param name="Name">The symbol naming the language.</param>
/// <param name="Entry">The symbol naming the entry nonterminal.</param>
/// <param name="Terminals">The terminal descriptors declared by the language.</param>
/// <param name="NonTerminals">The nonterminal descriptors declared by the language.</param>
public sealed record LanguageDescriptor(
    Symbol Name,
    Symbol Entry,
    Seq<TerminalDescriptor> Terminals,
    Seq<NonTerminalDescriptor> NonTerminals);
