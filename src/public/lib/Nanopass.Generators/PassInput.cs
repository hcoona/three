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

using Microsoft.CodeAnalysis;

namespace Hcoona.Nanopass.Generators;

internal sealed record PassInput(
    string Name,
    string QualifiedName,
    string Namespace,
    string Accessibility,
    EquatableArray<string> Containers,
    bool CanEmit,
    LocationInfo Location,
    PassInput.Endpoint From,
    PassInput.Endpoint To,
    bool Trace,
    EquatableArray<PassInput.Method> Methods,
    EquatableArray<string> MemberNames,
    EquatableArray<PassInput.TypeConversion> Conversions,
    EquatableArray<PassInput.TypeProjection> Projections,
    EquatableArray<PassType> ReferenceTypes)
{
    internal sealed record Endpoint(string? Name, string? Metadata, bool IsRaw, string? Entry);

    internal sealed record Method(
        string Name,
        string Accessibility,
        bool IsStatic,
        bool IsPartialDeclaration,
        string ReturnModifier,
        PassType ReturnType,
        EquatableArray<string> TypeParameters,
        EquatableArray<Parameter> Parameters,
        LocationInfo Location,
        Processor? Processor,
        EquatableArray<Rewrite> Rewrites);

    internal sealed record Parameter(
        string Name,
        PassType Type,
        bool IsOptional,
        string Modifier = "",
        Recur? Recursion = null,
        LocationInfo Location = default,
        string? DefaultExpression = null)
    {
        internal bool CanOmit => this.IsOptional || this.Modifier == "params ";
    }

    internal sealed record Processor(
        string? From, string? To, EquatableArray<string>? Defaults, bool HasDefaults);

    internal sealed record Rewrite(
        PassType? Production, string? Processor, string? Guard, bool IsElse, int Order);

    internal sealed record Recur(
        string? Field, string? Processor, EquatableArray<string> Arguments);

    internal sealed record TypeConversion(string From, string To, bool IsReference);

    internal sealed record TypeProjection(
        PassType From, PassType AsTarget, EquatableArray<VarianceKind> Variances);
}
