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

internal sealed record PassModel(
    PassInput Declaration,
    LanguageModel? SourceLanguage,
    LanguageModel? TargetLanguage,
    PassModel.Kind? InputKind,
    PassModel.Kind? OutputKind,
    PassInput.Method? Run,
    string? EntryCall,
    string? InterfaceCall,
    EquatableArray<PassModel.Processor> Processors,
    EquatableArray<PassModel.Problem> Problems)
{
    internal sealed record Kind(
        string Name, PassType Type, LanguageModel.NonTerminal? NonTerminal);

    internal sealed record Processor(
        string Name,
        Kind From,
        Kind To,
        PassType Result,
        EquatableArray<PassInput.Parameter> Parameters,
        PassInput.Method? Method,
        EquatableArray<string> Defaults,
        EquatableArray<Alternative> Alternatives = default)
    {
        internal bool IsSynthesized => this.Method is null || this.Method.IsPartialDeclaration;
    }

    internal sealed record Clause(
        PassInput.Method Method, PassInput.Rewrite Rewrite, LanguageModel.Production? Rule);

    internal sealed record Alternative(
        string? Condition,
        EquatableArray<(string? Guard, string Call)> Rewrites,
        SynthesizedReturn? Fallback);

    internal sealed record SynthesizedReturn(
        string Expression,
        (string Variable, string Call)? Subprocessor = null,
        bool UsesDefaults = false);

    internal sealed record Problem(
        int Number, LocationInfo Location, string Message, string? Stub = null);
}
