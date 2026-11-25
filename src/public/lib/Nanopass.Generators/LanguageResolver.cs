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
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace Hcoona.Nanopass.Generators;

internal sealed class LanguageResolver(ImmutableArray<LanguageInput> inputs)
{
    private static readonly string[] RecordMemberNames =
    [
        "EqualityContract", "PrintMembers", "ToString", "GetHashCode", "Equals",
        "ReferenceEquals", "GetType", "MemberwiseClone", "Deconstruct", "Clone",
    ];

    private static readonly string[] UnionMemberNames =
    [
        "Value", "RawValue", "HasValue", "TryGetValue", "IUnionMembers", "Equals",
        "ReferenceEquals", "GetHashCode", "GetType", "ToString", "MemberwiseClone",
    ];

    private readonly Dictionary<string, Result> resolved = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = new(StringComparer.Ordinal);

    internal ImmutableArray<Result> ResolveAll() => [.. inputs.Select(this.Resolve)];

    private static void ApplyRemovals(
        LanguageInput input,
        LanguageModel? inherited,
        List<LanguageModel.Terminal> terminals,
        List<NonTerminalBuilder> nonTerminals)
    {
        foreach (var removal in input.Removals)
        {
            bool removed = false;
            if (removal.IsTerminal)
            {
                removed = terminals.RemoveAll(terminal =>
                    Equal(removal.Name, removal.IsType ? terminal.Type : terminal.Name)) > 0;
            }
            else if (inherited is not null)
            {
                foreach (var nonTerminal in nonTerminals.ToArray())
                {
                    if (removal.Owner is not null && !Equal(removal.Owner, nonTerminal.Name))
                    {
                        continue;
                    }

                    if (Equal(removal.Name, inherited.QualifiedName + "." + nonTerminal.Name))
                    {
                        nonTerminals.Remove(nonTerminal);
                        removed = true;
                    }
                    else
                    {
                        removed |= nonTerminal.Productions.RemoveAll(rule =>
                            rule.Alternative is null
                            && Equal(removal.Name, inherited.QualifiedName + "." + rule.RecordPath))
                            > 0;
                    }
                }
            }

            if (!removed)
            {
                var kind = removal.IsTerminal ? "terminal" : "production or nonterminal";
                throw new Failure(3, $"Unrecognized {kind} in subtract: '{removal.Name}'.");
            }
        }

        foreach (var clause in input.Clauses)
        {
            var nonTerminal = nonTerminals.FirstOrDefault(item => Equal(item.Name, clause.Name));
            foreach (string remove in clause.Remove)
            {
                string syntax = PatternDatum.Parse(remove).Syntax.ToString();
                if (nonTerminal?.Productions.RemoveAll(rule => Equal(rule.Syntax, syntax)) > 0)
                {
                    continue;
                }

                if (input.NativeNonTerminals.Any(item => Equal(item.Name, clause.Name)
                    && item.Declaration == LanguageModel.Declaration.Union))
                {
                    var message = $"Cannot subtract '{syntax}' from partial union '{clause.Name}' "
                        + "before its cases are declared.";
                    throw new Failure(5, message);
                }

                var missing = nonTerminal is null
                    ? $"Unrecognized nonterminal in subtract: '{clause.Name}'."
                    : $"Unrecognized production in subtract: '{syntax}'.";
                throw new Failure(3, missing);
            }
        }
    }

    private static void DeclareClauses(
        LanguageInput input,
        LanguageModel? inherited,
        List<NonTerminalBuilder> nonTerminals,
        string? nativeEntry)
    {
        foreach (var clause in input.Clauses)
        {
            if (!SyntaxFacts.IsValidIdentifier(clause.Name))
            {
                throw new Failure(5, $"Invalid nonterminal name '{clause.Name}'.");
            }

            if (!clause.Extend && clause.Add.Count == 0)
            {
                throw new Failure(5, $"Nonterminal '{clause.Name}' needs a production.");
            }

            var metaVars = NormalizeMetaVars(clause.MetaVars);
            var nonTerminal = nonTerminals.FirstOrDefault(item => Equal(item.Name, clause.Name));
            if (nonTerminal is null)
            {
                var extensions = input.Clauses.Where(item =>
                    item.Extend && Equal(item.Name, clause.Name));
                var native = input.NativeNonTerminals.FirstOrDefault(item =>
                    Equal(item.Name, clause.Name));
                bool anyAdd = clause.Add.Count > 0 || extensions.Any(item => item.Add.Count > 0);
                bool anyRemove = extensions.Any(item => item.Remove.Count > 0);
                bool anyMetaVars = extensions.Any(item => item.MetaVars.Count > 0)
                    || native is { MetaVars.Count: > 0 };
                if (!anyAdd && native is null && !Equal(clause.Name, nativeEntry))
                {
                    if (anyRemove)
                    {
                        continue;
                    }

                    throw new Failure(3, $"Unrecognized nonterminal '{clause.Name}'.");
                }

                if (clause.Extend && !anyMetaVars)
                {
                    metaVars = InheritedMetaVars(inherited, clause.Name);
                }

                nonTerminal = new(
                    clause.Name, metaVars, LanguageModel.Declaration.None);
                nonTerminals.Add(nonTerminal);
            }
            else if (!clause.Extend)
            {
                throw new Failure(4, $"Duplicate nonterminal in add: '{clause.Name}'.");
            }
            else if (metaVars.Count > 0)
            {
                nonTerminal.MetaVars = new(nonTerminal.MetaVars.Concat(metaVars)
                    .Distinct(StringComparer.Ordinal));
            }
        }
    }

    private static void AddClauseProductions(
        EquatableArray<LanguageInput.Clause> clauses,
        List<NonTerminalBuilder> nonTerminals,
        List<LanguageModel.Terminal> terminals)
    {
        var metas = IndexMetaVars(terminals, nonTerminals);
        foreach (var clause in clauses.Where(static clause => clause.Add.Count > 0))
        {
            var nonTerminal = nonTerminals.First(item => Equal(item.Name, clause.Name));
            foreach (string add in clause.Add)
            {
                AddProduction(nonTerminal, ProductionReader.Read(add, metas));
            }
        }
    }

    private static void AddProduction(
        NonTerminalBuilder nonTerminal, LanguageModel.Production production)
    {
        if (nonTerminal.Productions.Any(item => Equal(item.Syntax, production.Syntax)))
        {
            throw new Failure(4, $"Duplicate production in add: '{production.Syntax}'.");
        }

        nonTerminal.Productions.Add(production);
    }

    private static Dictionary<string, LanguageModel.Field> IndexMetaVars(
        List<LanguageModel.Terminal> terminals, List<NonTerminalBuilder> nonTerminals)
    {
        Dictionary<string, LanguageModel.Field> metas = new(StringComparer.Ordinal);
        foreach (var terminal in terminals)
        {
            Add(terminal.Name, terminal.MetaVars, isNonTerminal: false);
        }

        foreach (var nonTerminal in nonTerminals)
        {
            Add(nonTerminal.Name, nonTerminal.MetaVars, isNonTerminal: true);
        }

        return metas;

        void Add(string target, EquatableArray<string> names, bool isNonTerminal)
        {
            if (names.Count == 0)
            {
                throw new Failure(5, $"Expected metavariables for '{target}'.");
            }

            foreach (string name in names)
            {
                if (metas.ContainsKey(name))
                {
                    throw new Failure(4, $"Duplicate metavariable in add: '{name}'.");
                }

                metas.Add(name, new(name, string.Empty, target, isNonTerminal, 0, false));
            }
        }
    }

    private static EquatableArray<string> NormalizeMetaVars(EquatableArray<string> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(names.Count);
        foreach (string spelling in names)
        {
            var (datum, pretty) = PatternDatum.Parse(spelling);
            if (!datum.IsSymbol || datum.Atom is not { Length: > 0 } name
                || name is "." or "..." or "maybe" or "=>" or "->"
                || pretty is not null || !Equal(ProductionReader.BaseMetaVar(name), name))
            {
                throw new Failure(5, $"Invalid metavariable declaration '{spelling}'.");
            }

            if (!seen.Add(name))
            {
                throw new Failure(4, $"Duplicate metavariable in add: '{name}'.");
            }

            normalized.Add(name);
        }

        return new(normalized);
    }

    private static void DeclareNative(
        LanguageInput input, LanguageModel? inherited, List<NonTerminalBuilder> nonTerminals)
    {
        foreach (var native in input.NativeNonTerminals)
        {
            if (native.Declaration == LanguageModel.Declaration.Union
                && Equal(native.Name, "Value"))
            {
                var message = "A partial union cannot be named 'Value'. "
                    + "Use '[NonTerminal] public readonly partial struct Value' or "
                    + "[NonTerminalDef] instead.";
                throw new Failure(5, message);
            }

            var target = nonTerminals.FirstOrDefault(item => Equal(item.Name, native.Name));
            var meta = native.MetaVars.Count > 0
                ? NormalizeMetaVars(native.MetaVars)
                : target is { MetaVars.Count: > 0 }
                    ? target.MetaVars
                    : InheritedMetaVars(inherited, native.Name);
            if (meta.Count == 0)
            {
                meta = new([native.Name.Substring(0, 1).ToLowerInvariant()]);
            }

            if (target is null)
            {
                nonTerminals.Add(new(native.Name, meta, native.Declaration));
            }
            else
            {
                target.MetaVars = native.MetaVars.Count > 0 && target.MetaVars.Count > 0
                    ? new(target.MetaVars.Concat(meta).Distinct(StringComparer.Ordinal))
                    : meta;
                target.Declared = native.Declaration;
            }
        }
    }

    private static void ApplyNative(
        LanguageInput input,
        LanguageModel? inherited,
        List<LanguageModel.Terminal> terminals,
        List<NonTerminalBuilder> nonTerminals,
        string? nativeEntry)
    {
        var seenRecords = new HashSet<string>(StringComparer.Ordinal);
        var declaredTerminalTypes = new HashSet<string>(
            terminals.Select(static terminal => terminal.Type), StringComparer.Ordinal);
        foreach (var native in input.NativeNonTerminals)
        {
            var target = nonTerminals.First(item => Equal(item.Name, native.Name));
            foreach (var alternative in native.Cases)
            {
                if (alternative.Type.EndsWith("?", StringComparison.Ordinal))
                {
                    throw new Failure(5, $"Union case '{alternative.Type}' cannot be nullable.");
                }

                var record = declaredTerminalTypes.Contains(alternative.Type)
                    ? null
                    : input.NativeRecords.FirstOrDefault(item =>
                        Equal(item.Type, alternative.Type));
                if (record is not null)
                {
                    AddProduction(target, ReadNativeRecord(record));
                    seenRecords.Add(record.Path);
                }
                else
                {
                    LanguageModel.Field field = FindTarget(alternative);
                    var symbol = new PatternDatum(
                        field.MetaVar, default, null, IsEscapedSymbol: true);
                    AddProduction(target, new(
                        string.Empty,
                        symbol.ToString(),
                        null,
                        null,
                        default,
                        field,
                        string.Empty,
                        false,
                        false));
                }
            }
        }

        if (input.NativeNonTerminals.Count == 0 && nativeEntry is null)
        {
            return;
        }

        foreach (var record in input.NativeRecords)
        {
            if (seenRecords.Contains(record.Path)
                || terminals.Any(terminal => Equal(terminal.Type, record.Type)))
            {
                continue;
            }

            var target = nonTerminals.FirstOrDefault(item =>
                record.Path.StartsWith(item.Name + ".", StringComparison.Ordinal));
            target ??= nativeEntry is not null
                ? nonTerminals.FirstOrDefault(item => Equal(item.Name, nativeEntry))
                : inherited is null && nonTerminals.Count == 1 ? nonTerminals[0] : null;
            if (target is null || target.Declared == LanguageModel.Declaration.Union)
            {
                continue;
            }

            AddProduction(target, ReadNativeRecord(record));
        }

        LanguageModel.Field FindTarget(LanguageInput.TypeRef typeRef)
        {
            var type = typeRef.Type;
            var nonTerminal = typeRef.UnqualifiedName is { } bare
                ? nonTerminals.FirstOrDefault(item => Equal(bare, item.Name))
                : null;
            nonTerminal ??= nonTerminals.FirstOrDefault(item =>
                Equal(type, input.QualifiedName + "." + LanguageEmitter.Id(item.Name)));
            if (nonTerminal is null)
            {
                var existing = terminals.FirstOrDefault(item => Equal(item.Type, type));
                if (existing is not null)
                {
                    return new(existing.MetaVars[0], string.Empty, existing.Name, false, 0, false);
                }

                if (inherited is not null)
                {
                    nonTerminal = nonTerminals.FirstOrDefault(item =>
                        Equal(type, inherited.QualifiedName + "." + LanguageEmitter.Id(item.Name)));
                }
            }

            if (nonTerminal is not null)
            {
                return new(nonTerminal.MetaVars[0], string.Empty, nonTerminal.Name, true, 0, false);
            }

            if (type.Length == 0)
            {
                throw new Failure(5, "A native production contains an unresolved field type.");
            }

            var shortType = type.Substring(type.LastIndexOf('.') + 1);
            var name = shortType.ToLowerInvariant();
            var meta = InferredMetaVar(shortType);
            if (!IsAvailable(name, meta))
            {
                var qualifiedName = type.Replace("global::", string.Empty);
                var qualifiedMeta = InferredMetaVar(qualifiedName);
                name = qualifiedName;
                meta = qualifiedMeta;
                for (int suffix = 2; !IsAvailable(name, meta); suffix++)
                {
                    var index = suffix.ToString(CultureInfo.InvariantCulture);
                    name = qualifiedName + index;
                    meta = qualifiedMeta + index + "Term";
                }
            }

            terminals.Add(new(name, type, new([meta]), null, null, input.QualifiedName));
            return new(meta, string.Empty, name, false, 0, false);
        }

        static string InferredMetaVar(string typeName)
        {
            // Keep the terminal metavariable intact when parsing strips the field index.
            var meta = "t" + ProductionReader.Identifier(typeName, "Terminal");
            return char.IsLetter(meta[meta.Length - 1]) ? meta : meta + "Term";
        }

        bool IsAvailable(string name, string meta) =>
            !terminals.Any(item => Equal(item.Name, name)
                || item.MetaVars.Contains(meta, StringComparer.Ordinal))
            && !nonTerminals.Any(item => item.MetaVars.Contains(meta, StringComparer.Ordinal));

        string TerminalType(string name) => terminals.First(item => Equal(item.Name, name)).Type;

        bool SameShape(LanguageModel.Field left, LanguageModel.Field right) =>
            left.IsNonTerminal == right.IsNonTerminal
            && left.SequenceDepth == right.SequenceDepth && left.IsMaybe == right.IsMaybe
            && (left.IsNonTerminal
                ? Equal(left.Target, right.Target)
                : Equal(TerminalType(left.Target), TerminalType(right.Target)));

        LanguageModel.Production ReadNativeRecord(LanguageInput.NativeRecord record)
        {
            var fields = record.Fields.Select(field => FindTarget(field.Type) with
            {
                Property = field.Name,
                SequenceDepth = field.SequenceDepth,
                IsMaybe = field.IsMaybe,
            }).ToArray();
            var syntax = record.Syntax;
            if (syntax is null)
            {
                var tag = record.SyntaxName ?? LowerInitial(record.Name);
                var defaultFields = fields.Select((field, index) => DefaultFieldSyntax(field with
                {
                    MetaVar = field.MetaVar + index.ToString(CultureInfo.InvariantCulture),
                }));
                syntax = "(" + string.Join(" ", new[] { tag }.Concat(defaultFields)) + ")";
            }

            var metas = IndexMetaVars(terminals, nonTerminals);
            foreach (var field in fields)
            {
                string alias = LowerInitial(field.Property);
                var meta = ProductionReader.BaseMetaVar(alias);
                if (!metas.ContainsKey(meta))
                {
                    metas.Add(meta, field);
                }
            }

            var production = ProductionReader.Read(syntax, metas);
            if (production.Pretty is not null && record.HasPrettyAttribute)
            {
                throw new Failure(5, $"'{record.Path}' specifies both inline => and [Pretty].");
            }

            if (production.Alternative is not null || production.Fields.Count != fields.Length)
            {
                throw new Failure(
                    5, $"The syntax for '{record.Path}' must bind every constructor parameter.");
            }

            var remaining = fields.ToList();
            var bound = new List<LanguageModel.Field>();
            foreach (var field in production.Fields)
            {
                var match = remaining.FirstOrDefault(item => SameShape(item, field)
                    && Equal(LowerInitial(item.Property), LowerInitial(field.MetaVar)))
                    ?? remaining.FirstOrDefault(item => SameShape(item, field))
                    ?? throw new Failure(
                        5, $"Field '{field.MetaVar}' has no parameter on '{record.Path}'.");
                remaining.Remove(match);
                bound.Add(field with { Property = match.Property });
            }

            string? pretty = record.Pretty is null
                ? production.Pretty
                : ProductionReader.ReadPretty(record.Pretty, new(bound));
            if (pretty is not null && record.PrettyHandler is not null)
            {
                throw new Failure(5, "A production accepts either a pretty template or a handler.");
            }

            return production with
            {
                Name = record.SyntaxName ?? record.Name,
                RecordPath = record.Path,
                Fields = new(bound),
                IsDeclared = true,
                Pretty = pretty,
                PrettyHandler = record.PrettyHandler is null
                    ? null
                    : new(record.PrettyHandler, input.QualifiedName),
            };
        }
    }

    private static string DefaultFieldSyntax(LanguageModel.Field field)
    {
        var symbol = new PatternDatum(field.MetaVar, default, null, IsEscapedSymbol: true);
        string syntax = field.IsMaybe ? "(maybe " + symbol + ")" : symbol.ToString();
        for (int i = 0; i < field.SequenceDepth; i++)
        {
            syntax = "(" + syntax + " ...)";
        }

        return syntax;
    }

    private static string LowerInitial(string name) =>
        name.Substring(0, 1).ToLowerInvariant() + name.Substring(1);

    private static bool Equal(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private static EquatableArray<string> InheritedMetaVars(
        LanguageModel? inherited, string name) =>
        inherited?.NonTerminals.FirstOrDefault(item => Equal(item.Name, name))?.MetaVars ?? default;

    private static void AssignRecordNames(
        LanguageInput input, List<NonTerminalBuilder> nonTerminals)
    {
        foreach (var nonTerminal in nonTerminals)
        {
            var names = new HashSet<string>(
                nonTerminal.Productions.Select(static rule => rule.RecordPath),
                StringComparer.Ordinal);
            var groups = nonTerminal.Productions
                .Where(static rule => rule.Alternative is null)
                .GroupBy(static rule => rule.RecordPath, StringComparer.Ordinal).ToArray();
            foreach (var group in groups)
            {
                if (group.Count() < 2)
                {
                    continue;
                }

                int suffix = 2;
                foreach (var production in group.Where(static rule => !rule.IsDeclared))
                {
                    var arity = production.Fields.Count.ToString(CultureInfo.InvariantCulture);
                    var name = group.Key + arity;
                    while (!names.Add(name))
                    {
                        name = group.Key + suffix++.ToString(CultureInfo.InvariantCulture);
                    }

                    int index = nonTerminal.Productions.IndexOf(production);
                    nonTerminal.Productions[index] = production with
                    {
                        Name = name,
                        RecordPath = name,
                    };
                }
            }
        }

        var shared = new HashSet<string>(
            nonTerminals.SelectMany(static item => item.Productions)
                .Where(static rule => rule.Alternative is null)
                .GroupBy(static rule => rule.RecordPath, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1).Select(static group => group.Key),
            StringComparer.Ordinal);
        var nonTerminalNames = new HashSet<string>(
            nonTerminals.Select(static item => item.Name), StringComparer.Ordinal);
        if (Equal(input.Name, LanguageEmitter.RuntimeMemberName)
            || LanguageEmitter.PublicStaticMemberNames(nonTerminalNames)
                .Contains(input.Name, StringComparer.Ordinal))
        {
            throw new Failure(
                5, $"Language name '{input.Name}' conflicts with a generated language member.");
        }

        var reserved = new HashSet<string>(
            LanguageEmitter.StaticMemberNames(nonTerminalNames), StringComparer.Ordinal)
        {
            input.Name,
        };
        foreach (var nonTerminal in nonTerminals)
        {
            if (reserved.Contains(nonTerminal.Name)
                || (nonTerminal.Declared == LanguageModel.Declaration.None
                    && input.MemberNames.Contains(nonTerminal.Name, StringComparer.Ordinal)))
            {
                throw new Failure(
                    5, $"Nonterminal '{nonTerminal.Name}' conflicts with a language member.");
            }
        }

        reserved.UnionWith(input.MemberNames);
        var occupied = new HashSet<string>(
            nonTerminals.SelectMany(static item => item.Productions)
                .Select(static rule => rule.RecordPath),
            StringComparer.Ordinal);
        occupied.UnionWith(nonTerminalNames);
        occupied.UnionWith(reserved);
        occupied.UnionWith(input.NativeRecords.Select(static item => item.Path));
        occupied.UnionWith(nonTerminalNames.SelectMany(static name => UnionMemberNames
            .Append(name).Select(member => name + "." + member)));
        foreach (var nonTerminal in nonTerminals)
        {
            for (int i = 0; i < nonTerminal.Productions.Count; i++)
            {
                var rule = nonTerminal.Productions[i];
                if (rule.Alternative is not null || rule.IsDeclared)
                {
                    continue;
                }

                string path = rule.RecordPath;
                int dot = path.LastIndexOf('.');
                if (dot < 0 && (shared.Contains(path) || nonTerminalNames.Contains(path)))
                {
                    dot = nonTerminal.Name.Length;
                    path = nonTerminal.Name + "." + path;
                }

                if (dot >= 0)
                {
                    var name = path.Substring(dot + 1);
                    if (Equal(name, nonTerminal.Name)
                        || UnionMemberNames.Contains(name, StringComparer.Ordinal)
                        || (!Equal(path, rule.RecordPath) && !occupied.Add(path)))
                    {
                        path = UniqueNodePath(path);
                    }
                }
                else if (reserved.Contains(path))
                {
                    path = UniqueNodePath(path);
                }

                var nameAfterRename = path.Substring(path.LastIndexOf('.') + 1);
                nonTerminal.Productions[i] = rule with
                {
                    RecordPath = path,
                    Fields = DisambiguateRecordFields(rule.Fields, nameAfterRename),
                };
            }
        }

        string UniqueNodePath(string path)
        {
            var stem = path + "Node";
            var name = stem;
            for (int suffix = 2; !occupied.Add(name); suffix++)
            {
                name = stem + suffix.ToString(CultureInfo.InvariantCulture);
            }

            return name;
        }
    }

    private static EquatableArray<LanguageModel.Field> DisambiguateRecordFields(
        EquatableArray<LanguageModel.Field> fields, string recordName)
    {
        var reserved = new HashSet<string>(RecordMemberNames, StringComparer.Ordinal)
        {
            recordName,
        };
        if (!fields.Any(field => reserved.Contains(field.Property)))
        {
            return fields;
        }

        var occupied = new HashSet<string>(reserved, StringComparer.Ordinal);
        occupied.UnionWith(fields.Select(static field => field.Property));
        return new(fields.Select(field =>
        {
            if (!reserved.Contains(field.Property))
            {
                return field;
            }

            var property = field.Property;
            for (int suffix = 2; !occupied.Add(property); suffix++)
            {
                property = field.Property + suffix.ToString(CultureInfo.InvariantCulture);
            }

            return field with { Property = property };
        }));
    }

    private static LanguageModel Prune(LanguageModel source)
    {
        var reachedNonTerminals = new HashSet<string>(StringComparer.Ordinal);
        var reachedTerminals = new HashSet<string>(StringComparer.Ordinal);
        Visit(source.Entry);
        return source with
        {
            Terminals = new(source.Terminals.Where(
                terminal => reachedTerminals.Contains(terminal.Name))),
            NonTerminals = new(source.NonTerminals.Where(
                nonTerminal => reachedNonTerminals.Contains(nonTerminal.Name))),
        };

        void Visit(string name)
        {
            if (!reachedNonTerminals.Add(name))
            {
                return;
            }

            var nonTerminal = source.NonTerminals.First(item => Equal(item.Name, name));
            foreach (var rule in nonTerminal.Productions)
            {
                var fields = rule.Alternative is null ? rule.Fields : new([rule.Alternative]);
                foreach (var field in fields)
                {
                    if (field.IsNonTerminal)
                    {
                        Visit(field.Target);
                    }
                    else
                    {
                        reachedTerminals.Add(field.Target);
                    }
                }
            }
        }
    }

    private Result Resolve(LanguageInput input)
    {
        if (this.resolved.TryGetValue(input.QualifiedName, out var existing))
        {
            return existing;
        }

        if (!this.active.Add(input.QualifiedName))
        {
            throw new Failure(
                2, $"Cyclic language inheritance involving '{input.Name}'.", isCycle: true);
        }

        Result result;
        try
        {
            result = new(this.ResolveModel(input), input.Location, 0, null, false);
        }
        catch (Failure error)
        {
            result = new(null, input.Location, error.Number, error.Message, error.IsCycle);
        }
        catch (FormatException error)
        {
            result = new(null, input.Location, 5, error.Message, false);
        }
        finally
        {
            this.active.Remove(input.QualifiedName);
        }

        this.resolved.Add(input.QualifiedName, result);
        return result;
    }

    private LanguageModel ResolveModel(LanguageInput input)
    {
        if (!input.IsValidDeclaration)
        {
            throw new Failure(
                5, "A language must be a non-generic static partial class inside partial types.");
        }

        if (input.Bases.Count > 1 || input.Entries.Count > 1)
        {
            throw new Failure(2, "Only one base/source and one entry may be declared.");
        }

        LanguageModel? inherited = null;
        if (input.Bases.Count == 1)
        {
            var source = input.Bases[0];
            var local = inputs.FirstOrDefault(candidate =>
                Equal(candidate.QualifiedName, source.Type));
            var localSource = local is null ? null : this.Resolve(local);
            if (localSource is { IsCycle: true })
            {
                throw new Failure(2, localSource.Message!, isCycle: true);
            }

            inherited = localSource is not null
                ? localSource.Model
                : source.Metadata is null ? null : LanguageMetadataCodec.Decode(source.Metadata);
            if (inherited is null || !Equal(inherited.QualifiedName, source.Type))
            {
                throw new Failure(1, $"Unrecognized base/source language '{source.Type}'.");
            }

            if (source.Prune)
            {
                inherited = Prune(inherited);
            }
        }

        List<LanguageModel.Terminal> terminals = [];
        List<NonTerminalBuilder> nonTerminals = [];
        if (inherited is not null)
        {
            terminals.AddRange(inherited.Terminals.Select(terminal =>
                terminal with { Owner = inherited.QualifiedName }));
            foreach (var item in inherited.NonTerminals)
            {
                var previous = new NonTerminalBuilder(
                    item.Name, item.MetaVars, LanguageModel.Declaration.None)
                {
                    Productions =
                    [
                        .. item.Productions.Select(static rule => rule with { IsDeclared = false }),
                    ],
                };
                nonTerminals.Add(previous);
            }
        }

        ApplyRemovals(input, inherited, terminals, nonTerminals);
        nonTerminals.RemoveAll(static item => item.Productions.Count == 0);

        var claimedTypes = new HashSet<string>(
            terminals.Concat(input.Terminals).Select(static terminal => terminal.Type),
            StringComparer.Ordinal);
        claimedTypes.UnionWith(input.NativeNonTerminals.SelectMany(static item => item.Cases)
            .Select(static type => type.Type));
        bool hasUnclaimedRecord = input.NativeRecords.Any(record => record.Path.IndexOf('.') < 0
            && !claimedTypes.Contains(record.Type));
        var nativeEntry = hasUnclaimedRecord
            ? (input.Entries.Count == 1 ? input.Entries[0] : inherited?.Entry)
            : null;

        DeclareClauses(input, inherited, nonTerminals, nativeEntry);
        DeclareNative(input, inherited, nonTerminals);
        if (nativeEntry is not null)
        {
            var fallback = nonTerminals.FirstOrDefault(item => Equal(item.Name, nativeEntry));
            if (fallback is null)
            {
                nonTerminals.Add(new(
                    nativeEntry,
                    InheritedMetaVars(inherited, nativeEntry),
                    LanguageModel.Declaration.None));
            }
            else if (fallback.MetaVars.Count == 0)
            {
                fallback.MetaVars = InheritedMetaVars(inherited, nativeEntry);
            }
        }

        foreach (var nonTerminal in nonTerminals)
        {
            if (Equal(nonTerminal.Name, "IUnionMembers"))
            {
                throw new Failure(
                    5, "Nonterminal name 'IUnionMembers' is reserved for union members.");
            }

            if (nonTerminal.MetaVars.Count == 0)
            {
                throw new Failure(5, $"Expected metavariables for '{nonTerminal.Name}'.");
            }
        }

        foreach (var terminal in input.Terminals)
        {
            if (terminal.Type.Length == 0 || terminal.MetaVars.Count == 0
                || !PatternDatum.Parse(terminal.Name).Syntax.IsSymbol)
            {
                throw new Failure(5, $"Unrecognized terminal syntax for '{terminal.Name}'.");
            }

            if (terminals.Any(item => Equal(item.Name, terminal.Name)))
            {
                throw new Failure(4, $"Duplicate terminal in add: '{terminal.Name}'.");
            }

            terminals.Add(terminal with { MetaVars = NormalizeMetaVars(terminal.MetaVars) });
        }

        ApplyNative(input, inherited, terminals, nonTerminals, nativeEntry);
        AddClauseProductions(input.Clauses, nonTerminals, terminals);
        nonTerminals.RemoveAll(static item => item.Productions.Count == 0);
        var entry = input.Entries.Count == 1
            ? input.Entries[0]
            : inherited?.Entry ?? nonTerminals.FirstOrDefault()?.Name;
        var metas = IndexMetaVars(terminals, nonTerminals);
        foreach (var nonTerminal in nonTerminals)
        {
            for (int i = 0; i < nonTerminal.Productions.Count; i++)
            {
                var rule = nonTerminal.Productions[i];
                nonTerminal.Productions[i] = rule with
                {
                    Fields = new(rule.Fields.Select(Rebind)),
                    Alternative = rule.Alternative is null ? null : Rebind(rule.Alternative),
                };
            }
        }

        AssignRecordNames(input, nonTerminals);
        var model = new LanguageModel(
            input.Name,
            input.QualifiedName,
            input.Namespace,
            input.Accessibility,
            input.Containers,
            entry ?? string.Empty,
            new(terminals),
            new(nonTerminals.Select(static item => new LanguageModel.NonTerminal(
                item.Name, item.MetaVars, new(item.Productions), item.Declared))));
        foreach (var native in input.NativeNonTerminals)
        {
            if (native.Declaration != LanguageModel.Declaration.Union)
            {
                continue;
            }

            var nonTerminal = model.NonTerminals.FirstOrDefault(item =>
                Equal(item.Name, native.Name));
            var guidance = $"Use '[NonTerminal] public readonly partial struct {native.Name}' "
                + "or omit the union declaration.";
            if (nonTerminal is null)
            {
                var message = $"Partial union '{native.Name}' has no resolved cases. {guidance}";
                throw new Failure(5, message);
            }

            if (nonTerminal.Productions.Any(static rule => rule.Alternative is null
                && !rule.IsDeclared))
            {
                var message = $"Partial union '{native.Name}' cannot contain synthesized records. "
                    + guidance;
                throw new Failure(5, message);
            }

            var declaredCases = new HashSet<string>(
                native.Cases.Select(NormalizeCaseType), StringComparer.Ordinal);
            if (!declaredCases.SetEquals(LanguageEmitter.CaseTypes(model, nonTerminal)))
            {
                var message = $"Partial union '{native.Name}' must list all its resolved cases "
                    + $"and no others. {guidance}";
                throw new Failure(5, message);
            }
        }

        if (entry is null || !nonTerminals.Any(item => Equal(item.Name, entry)))
        {
            throw new Failure(2, $"Unrecognized entry nonterminal '{entry}'.");
        }

        return model;

        string NormalizeCaseType(LanguageInput.TypeRef typeRef)
        {
            var sibling = typeRef.UnqualifiedName is { } bare
                ? model.NonTerminals.FirstOrDefault(item => Equal(bare, item.Name))
                : null;
            return sibling is null
                ? typeRef.Type
                : model.QualifiedName + "." + LanguageEmitter.Id(sibling.Name);
        }

        LanguageModel.Field Rebind(LanguageModel.Field field)
        {
            if (field.IsNonTerminal
                ? nonTerminals.Any(item => Equal(item.Name, field.Target))
                : terminals.Any(item => Equal(item.Name, field.Target)))
            {
                return field;
            }

            if (metas.TryGetValue(ProductionReader.BaseMetaVar(field.MetaVar), out var target))
            {
                return field with { Target = target.Target, IsNonTerminal = target.IsNonTerminal };
            }

            throw new Failure(
                5, $"Unrecognized metavariable '{field.MetaVar}' after grammar extension.");
        }
    }

    internal sealed record Result(
        LanguageModel? Model, LocationInfo Location, int Diagnostic, string? Message, bool IsCycle);

    private sealed class NonTerminalBuilder(
        string name,
        EquatableArray<string> metaVars,
        LanguageModel.Declaration declared)
    {
        internal string Name { get; } = name;

        internal EquatableArray<string> MetaVars { get; set; } = metaVars;

        internal LanguageModel.Declaration Declared { get; set; } = declared;

        internal List<LanguageModel.Production> Productions { get; set; } = [];
    }

    private sealed class Failure(int number, string message, bool isCycle = false)
        : Exception(message)
    {
        internal int Number { get; } = number;

        internal bool IsCycle { get; } = isCycle;
    }
}
