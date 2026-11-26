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

namespace Hcoona.Nanopass.Generators;

internal sealed record LanguageModel(
    string Name,
    string QualifiedName,
    string Namespace,
    string Accessibility,
    EquatableArray<string> Containers,
    string Entry,
    EquatableArray<LanguageModel.Terminal> Terminals,
    EquatableArray<LanguageModel.NonTerminal> NonTerminals)
{
    internal enum Declaration
    {
        None,
        Union,
        Struct,
    }

    internal sealed record Terminal(
        string Name,
        string Type,
        EquatableArray<string> MetaVars,
        string? Predicate,
        string? Prettifier,
        string Owner);

    internal sealed record NonTerminal(
        string Name,
        EquatableArray<string> MetaVars,
        EquatableArray<Production> Productions,
        Declaration Declared);

    internal sealed record Production(
        string Name,
        string Syntax,
        string? Pretty,
        Handler? PrettyHandler,
        EquatableArray<Field> Fields,
        Field? Alternative,
        string RecordPath,
        bool IsDeclared,
        bool IsImplicit);

    internal sealed record Handler(string Name, string Owner, string? Binding = null);

    internal sealed record Field(
        string MetaVar,
        string Property,
        string Target,
        bool IsNonTerminal,
        int SequenceDepth,
        bool IsMaybe);
}
