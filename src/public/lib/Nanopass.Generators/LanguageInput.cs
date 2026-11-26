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

internal sealed record LanguageInput(
    string Name,
    string QualifiedName,
    string Namespace,
    string Accessibility,
    EquatableArray<string> Containers,
    bool IsValidDeclaration,
    EquatableArray<LanguageInput.Inheritance> Bases,
    EquatableArray<string> Entries,
    EquatableArray<LanguageModel.Terminal> Terminals,
    EquatableArray<LanguageInput.Removal> Removals,
    EquatableArray<LanguageInput.Clause> Clauses,
    EquatableArray<LanguageInput.Native> NativeNonTerminals,
    EquatableArray<LanguageInput.NativeRecord> NativeRecords,
    EquatableArray<string> MemberNames)
{
    internal readonly record struct TypeRef(string Type, string? UnqualifiedName);

    internal sealed record Inheritance(string Type, string? Metadata, bool Prune);

    internal sealed record Removal(string? Owner, string Name, bool IsTerminal, bool IsType);

    internal sealed record Clause(
        string Name,
        EquatableArray<string> MetaVars,
        EquatableArray<string> Remove,
        EquatableArray<string> Add,
        bool Extend);

    internal sealed record Native(
        string Name,
        EquatableArray<string> MetaVars,
        EquatableArray<TypeRef> Cases,
        LanguageModel.Declaration Declaration);

    internal sealed record NativeRecord(
        string Name,
        string Path,
        string Type,
        string? Syntax,
        string? SyntaxName,
        string? Pretty,
        string? PrettyHandler,
        bool HasPrettyAttribute,
        EquatableArray<NativeField> Fields);

    internal sealed record NativeField(string Name, TypeRef Type, int SequenceDepth, bool IsMaybe);
}
