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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Hcoona.Nanopass.Generators;

internal sealed class PassGrammar
{
    private readonly Dictionary<string, PassModel.Kind> kinds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PassModel.Kind> terminalKinds = new(StringComparer.Ordinal);

    internal PassGrammar(PassInput.Endpoint endpoint, LanguageModel? language)
    {
        this.Endpoint = endpoint;
        if (endpoint.IsRaw)
        {
            this.kinds.Add("*", new("*", new(PassInputReader.Runtime + "SExpr"), null));
        }

        this.Language = language
            ?? (endpoint.Metadata is { } metadata ? LanguageMetadataCodec.Decode(metadata) : null);
        if (this.Language is not { } grammar)
        {
            return;
        }

        foreach (var nonTerminal in grammar.NonTerminals)
        {
            this.kinds.Add(nonTerminal.Name, new(
                nonTerminal.Name, this.NestedType(nonTerminal.Name), nonTerminal));
        }

        foreach (var terminal in grammar.Terminals)
        {
            this.terminalKinds.Add(terminal.Name, new(terminal.Name, new(terminal.Type), null));
        }
    }

    internal PassInput.Endpoint Endpoint { get; }

    internal LanguageModel? Language { get; }

    internal string? Entry =>
        this.Endpoint.Entry ?? (this.Endpoint.IsRaw ? "*" : this.Language?.Entry);

    internal IEnumerable<PassModel.Kind> Kinds =>
        this.kinds.Values.Concat(this.terminalKinds.Values);

    internal static ImmutableArray<LanguageModel.Production> Rules(PassModel.Kind kind) =>
        kind.NonTerminal is { } nonTerminal ? [.. nonTerminal.Productions] : [];

    internal static string ValueProperty(PassModel.Kind kind) =>
        kind.NonTerminal?.Declared == LanguageModel.Declaration.Struct
        || string.Equals(kind.Name, "Value", StringComparison.Ordinal)
            ? "RawValue"
            : "Value";

    internal static string Shape(LanguageModel.Production rule)
    {
        var fields = rule.Fields.Select(static field => field.MetaVar)
            .ToImmutableHashSet(StringComparer.Ordinal);
        return Render(PatternDatum.Parse(rule.Syntax).Syntax, !rule.IsImplicit);

        string Render(PatternDatum pattern, bool keepTag)
        {
            if (pattern.Atom is { } atom)
            {
                return !keepTag && fields.Contains(atom) ? "$" : pattern.ToString();
            }

            var parts = string.Join(" ", pattern.Items.Select(
                (item, index) => Render(item, keepTag && index == 0)));
            return pattern.Tail is null
                ? $"({parts})"
                : $"({parts} . {Render(pattern.Tail, false)})";
        }
    }

    internal PassModel.Kind? Find(string? name) =>
        name is null ? null : this.FindNonTerminal(name) ?? this.FindTerminal(name);

    internal PassModel.Kind? Find(string name, PassType type)
    {
        var kind = this.FindNonTerminal(name);
        if (PassInputReader.Same(kind?.Type, type))
        {
            return kind;
        }

        var terminal = this.FindTerminal(name);
        return PassInputReader.Same(terminal?.Type, type) ? terminal : null;
    }

    internal PassModel.Kind? Find(LanguageModel.Field field) =>
        field.IsNonTerminal ? this.FindNonTerminal(field.Target) : this.FindTerminal(field.Target);

    internal PassModel.Kind? Infer(PassType type, string? name = null)
    {
        var matches = this.Kinds.Where(kind => PassInputReader.Same(kind.Type, type)).ToArray();
        if (matches.Length <= 1)
        {
            return matches.FirstOrDefault();
        }

        var named = matches.Where(kind => kind.NonTerminal is null
            && this.MatchesTerminal(kind.Name, name)).Take(2).ToArray();
        return named.Length == 1 ? named[0] : null;
    }

    internal bool MatchesTerminal(string terminalName, string? identifier) =>
        identifier is not null && this.Terminal(terminalName) is { } terminal
        && (string.Equals(terminalName, identifier, StringComparison.Ordinal)
            || terminal.MetaVars.Contains(identifier, StringComparer.Ordinal));

    internal LanguageModel.Terminal? Terminal(string name) =>
        this.Language?.Terminals.FirstOrDefault(terminal => terminal.Name == name);

    internal bool CanConvert(PassType from, PassType to) =>
        this.Infer(to) is { NonTerminal: not null } kind
        && Rules(kind).Any(rule => PassInputReader.Same(this.RuleType(rule), from));

    internal PassType? RuleType(LanguageModel.Production rule)
    {
        if (rule.Alternative is { } alternative)
        {
            return this.Find(alternative)?.Type;
        }

        return this.NestedType(rule.RecordPath);
    }

    private PassType NestedType(string path) => new(this.Language!.QualifiedName
        + "." + string.Join(".", path.Split('.').Select(PassInputReader.Id)));

    private PassModel.Kind? FindNonTerminal(string name) =>
        this.kinds.TryGetValue(name, out var kind) ? kind : null;

    private PassModel.Kind? FindTerminal(string name) =>
        this.terminalKinds.TryGetValue(name, out var terminal) ? terminal : null;
}
