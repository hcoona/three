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

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Hcoona.Nanopass.Generators;

internal sealed record PassModel(
    PassInput Declaration,
    LanguageModel? SourceLanguage,
    LanguageModel? TargetLanguage,
    PassModel.Kind? InputKind,
    PassModel.Kind? OutputKind,
    PassInput.Method? Run,
    string? EntryCall,
    string? InterfaceCall,
    ImmutableArray<PassModel.Processor> Processors,
    ImmutableArray<PassModel.Problem> Problems)
{
    internal sealed record Kind(
        string Name, PassType Type, LanguageModel.NonTerminal? NonTerminal);

    internal sealed class Processor(
        string name,
        Kind from,
        Kind to,
        PassType result,
        ImmutableArray<PassInput.Parameter> parameters,
        PassInput.Method? method,
        EquatableArray<string> defaults)
    {
        internal string Name { get; } = name;

        internal Kind From { get; } = from;

        internal Kind To { get; } = to;

        internal PassType Result { get; } = result;

        internal ImmutableArray<PassInput.Parameter> Parameters { get; } = parameters;

        internal PassInput.Method? Method { get; } = method;

        internal EquatableArray<string> Defaults { get; } = defaults;

        internal List<Clause> Clauses { get; } = [];

        internal List<Alternative> Alternatives { get; } = [];

        internal bool IsSynthesized => this.Method is null || this.Method.IsPartialDeclaration;
    }

    internal sealed record Clause(
        PassInput.Method Method, PassInput.Rewrite Rewrite, LanguageModel.Production? Rule);

    internal sealed record Alternative(
        string? Condition,
        ImmutableArray<(string? Guard, string Call)> Rewrites,
        SynthesizedReturn? Fallback);

    internal sealed record SynthesizedReturn(
        string Expression, (string Variable, string Call)? Subprocessor = null);

    internal sealed record Problem(
        int Number, LocationInfo Location, string Message, string? Stub = null);
}
