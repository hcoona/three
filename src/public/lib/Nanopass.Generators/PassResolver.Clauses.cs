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

using static Hcoona.Nanopass.Generators.PassInputReader;

namespace Hcoona.Nanopass.Generators;

internal sealed partial class PassResolver
{
    private static string FieldExpression(
        LanguageModel.Production rule, LanguageModel.Field field) =>
        rule.Alternative is not null ? NodeVariable : $"{NodeVariable}.{Id(field.Property)}";

    private static EquatableArray<LanguageModel.Field> Fields(LanguageModel.Production? rule) =>
        rule?.Alternative is { } alternative ? new([alternative]) : rule?.Fields ?? default;

    private static string WithDefaults(PassModel.Processor processor, string expression)
    {
        if (processor.Defaults.Count == 0)
        {
            return expression;
        }

        var defaults = string.Join(", ", processor.Defaults);
        return $"({expression}, {defaults})";
    }

    private static PassType? ElementType(PassType type, int depth, bool maybe)
    {
        for (int i = 0; i < depth; i++)
        {
            if (type.Name != Runtime + "Seq" || type.Arguments.Count != 1)
            {
                return null;
            }

            type = type.Arguments[0];
        }

        if (maybe)
        {
            return type.Nullable ? type with { Nullable = false } : null;
        }

        return type;
    }

    private static string Map(
        LanguageModel.Field field,
        string inputExpression,
        string returnType,
        Func<string, string> transform)
    {
        return MapLevel(field.SequenceDepth, inputExpression);

        string MapLevel(int depth, string expression)
        {
            string item = "__np_item" + depth.ToString(CultureInfo.InvariantCulture);
            if (depth > 0)
            {
                return $"{expression}.Select({item} => {MapLevel(depth - 1, item)})";
            }

            return field.IsMaybe
                ? $"({expression}) switch {{ {{ }} {item} => {transform(item)}, "
                    + $"null => ({returnType}?)null }}"
                : transform(expression);
        }
    }

    private static LanguageModel.Field? FindField(LanguageModel.Production? rule, string name)
    {
        var matches = Fields(rule).Where(field =>
            string.Equals(field.MetaVar, name, StringComparison.Ordinal)
            || string.Equals(field.Property, name, StringComparison.Ordinal)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private void Build(PassModel.Processor processor)
    {
        if (!processor.IsSynthesized)
        {
            return;
        }

        var fallback = this.BuildClauses(processor, null);
        bool totalFallback = fallback.Any(static clause => clause.Guard is null);
        var rules = PassGrammar.Rules(processor.From).OrderByDescending(rule =>
            rule.Alternative is { IsNonTerminal: false } terminal
            && this.source.Terminal(terminal.Target)?.Predicate is not null).ToArray();
        foreach (var rule in rules)
        {
            var clauses = this.BuildClauses(processor, rule);
            var terminal = rule.Alternative is { IsNonTerminal: false } field
                ? this.source.Terminal(field.Target)
                : null;
            var ruleType = this.source.RuleType(rule);
            bool overlaps = totalFallback && terminal?.Predicate is not null
                && processor.Clauses.Any(clause => clause.Rule is { } other && other != rule
                    && Same(ruleType, this.source.RuleType(other)));
            if (clauses.IsEmpty && totalFallback && !overlaps)
            {
                continue;
            }

            if (ruleType is null)
            {
                this.Invalid(processor.Method, $"cannot resolve production '{rule.Syntax}'.");
                continue;
            }

            PassModel.SynthesizedReturn? last = null;
            bool totalClause = clauses.Any(static clause => clause.Guard is null);
            if (!totalClause && !totalFallback)
            {
                last = this.Synthesize(processor, rule);
                if (last is null)
                {
                    this.Missing(processor, rule, ruleType);
                }
            }

            string condition = $"{InputName(processor)}.{PassGrammar.ValueProperty(processor.From)}"
                + $" is {Type(ruleType)} {NodeVariable}";
            if (terminal is { Predicate: not null })
            {
                string binding = LanguageEmitter.TerminalBinding(terminal);
                condition += $" && {this.source.Language!.QualifiedName}.{binding}()"
                    + $".Accept({NodeVariable})";
            }

            var rewrites = !totalClause && (!totalFallback || overlaps)
                ? clauses.AddRange(fallback)
                : clauses;
            processor.Alternatives.Add(new(condition, rewrites, last));
        }

        string? identity = null;
        if (rules.Length == 0 && !totalFallback)
        {
            if (processor.From.Name == processor.To.Name
                && this.Converts(processor.From.Type, processor.To.Type)
                && (!processor.Result.IsTuple || processor.Defaults.Count > 0))
            {
                identity = WithDefaults(processor, InputName(processor));
            }
            else if (fallback.IsEmpty)
            {
                this.Invalid(
                    processor.Method,
                    $"cannot synthesize '{processor.Name}' without a grammar production.");
            }
        }

        if (!fallback.IsEmpty || identity is not null)
        {
            processor.Alternatives.Add(new(
                null, fallback, identity is null ? null : new(identity)));
        }
    }

    private ImmutableArray<(string? Guard, string Call)> BuildClauses(
        PassModel.Processor processor, LanguageModel.Production? rule)
    {
        var clauses = processor.Clauses.Where(clause => clause.Rule == rule)
            .OrderBy(static clause => clause.Rewrite.Order);
        var result = ImmutableArray.CreateBuilder<(string? Guard, string Call)>();
        bool unguarded = false;
        foreach (var clause in clauses)
        {
            var guard = clause.Rewrite.Guard;
            if (unguarded)
            {
                string message = $"has unreachable rewrite '{clause.Method.Name}'.";
                this.Invalid(clause.Method, message);
                continue;
            }

            var arguments = this.RewriteArguments(processor, rule, clause.Method, allowRecur: true);
            string? guardCall = guard is null
                ? null
                : this.Guard(processor, rule, clause.Method, guard);
            if (arguments is { } args && (guard is null || guardCall is not null))
            {
                result.Add((guardCall, Call(clause.Method, args)));
                unguarded = guard is null;
            }
        }

        return result.ToImmutable();
    }

    private string? Guard(
        PassModel.Processor processor,
        LanguageModel.Production? rule,
        PassInput.Method clause,
        string name)
    {
        var matches = input.Methods.Where(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal)
            && candidate.ReturnType.Name == "bool"
            && candidate.TypeParameters.Count == 0 && candidate.ReturnModifier.Length == 0
            && candidate.Parameters.All(static item => IsValueParameter(item)))
            .Select(candidate => (Method: candidate,
                Args: this.RewriteArguments(processor, rule, candidate, allowRecur: false)))
            .Where(static candidate => candidate.Args is not null)
            .Take(2).ToArray();
        if (matches.Length == 1)
        {
            var match = matches[0];
            return Call(match.Method, match.Args!.Value);
        }

        this.Invalid(clause, $"has invalid or ambiguous guard '{name}'.");
        return null;
    }

    private ImmutableArray<string>? RewriteArguments(
        PassModel.Processor processor,
        LanguageModel.Production? rule,
        PassInput.Method method,
        bool allowRecur)
    {
        var arguments = ImmutableArray.CreateBuilder<string>();
        var nodeType = rule is null ? null : this.source.RuleType(rule);
        foreach (var parameter in method.Parameters)
        {
            string? expression;
            if (parameter.Recursion is { } recur)
            {
                if (!allowRecur)
                {
                    return null;
                }

                expression = this.Recursion(processor, rule, method, parameter, recur);
                if (expression is null)
                {
                    return null;
                }
            }
            else
            {
                var context = processor.Parameters.FirstOrDefault(item =>
                    string.Equals(item.Name, parameter.Name, StringComparison.Ordinal)
                    && Same(item.Type, parameter.Type));
                expression = context is not null
                    ? Id(context.Name)
                    : this.BindField(rule, parameter.Name, parameter.Type);
                bool inputMatches = Same(processor.From.Type, parameter.Type)
                    && (rule is null || parameter.Name == "ir");
                expression ??= inputMatches
                    ? InputName(processor)
                    : nodeType is not null && Same(nodeType, parameter.Type) ? NodeVariable : null;
            }

            if (expression is null)
            {
                if (parameter.IsOptional)
                {
                    continue;
                }

                if (allowRecur)
                {
                    this.Invalid(
                        method,
                        $"cannot bind parameter '{parameter.Name}' on '{method.Name}'.");
                }

                return null;
            }

            arguments.Add($"{Id(parameter.Name)}: {expression}");
        }

        return arguments.ToImmutable();
    }

    private string? BindField(LanguageModel.Production? rule, string name, PassType type)
    {
        if (rule is not null && FindField(rule, name) is { } field
            && Same(
                this.source.Find(field)?.Type,
                ElementType(type, field.SequenceDepth, field.IsMaybe)))
        {
            return FieldExpression(rule, field);
        }

        return null;
    }

    private string? Recursion(
        PassModel.Processor processor,
        LanguageModel.Production? rule,
        PassInput.Method method,
        PassInput.Parameter parameter,
        PassInput.Recur recur)
    {
        var fieldName = recur.Field ?? parameter.Name;
        var field = FindField(rule, fieldName);
        var from = field is null ? null : this.source.Find(field);
        var result = field is null
            ? null
            : ElementType(parameter.Type, field.SequenceDepth, field.IsMaybe);
        if (rule is null || field is null || from is null || result is null)
        {
            string message = $"has invalid recur field '{fieldName}' on '{method.Name}'.";
            this.Invalid(method, message);
            return null;
        }

        var requested = recur.Processor;
        var candidates = this.processors.Where(candidate => candidate.Method is not null
            && (requested is null
                || string.Equals(candidate.Name, requested, StringComparison.Ordinal))
            && candidate.From == from && Same(candidate.Result, result))
            .Select(candidate => (Processor: candidate, Binding: BindArguments(candidate),
                Named: this.target.MatchesTerminal(candidate.To.Name, parameter.Name)))
            .Where(static candidate => candidate.Binding is not null)
            .OrderByDescending(static candidate => candidate.Binding!.Value.Matched)
            .ThenByDescending(static candidate => candidate.Named)
            .Take(2).ToArray();
        PassModel.Processor? chosen = candidates.FirstOrDefault().Processor;
        if (candidates.Length == 2
            && candidates[0].Binding!.Value.Matched == candidates[1].Binding!.Value.Matched
            && candidates[0].Named == candidates[1].Named)
        {
            this.Invalid(method, $"has ambiguous recur processor for '{fieldName}'.");
            return null;
        }

        if (chosen is null && requested is null && recur.Arguments.Count == 0 && field.IsNonTerminal
            && this.target.Infer(result) is { NonTerminal: not null } to)
        {
            chosen = this.InferProcessor(from, to);
        }

        if (chosen is null)
        {
            if (!field.IsNonTerminal && requested is null && candidates.Length == 0)
            {
                this.problems.Add(new(
                    2,
                    parameter.Location,
                    $"Pass '{input.Name}' requires a terminal processor for '{from.Name}'."));
            }
            else
            {
                this.Invalid(
                    method,
                    $"has no applicable recur processor '{requested}' for field '{fieldName}'.");
            }

            return null;
        }

        var bound = BindArguments(chosen)!.Value.Arguments;
        return Map(
            field,
            FieldExpression(rule, field),
            Type(result),
            value => this.InvokeFrom(processor, chosen, value, bound));

        (ImmutableArray<string> Arguments, int Matched)? BindArguments(
            PassModel.Processor candidate) =>
            Bind(
                candidate,
                processor.Parameters,
                recur.Arguments,
                (name, type) => this.BindField(rule, name, type));
    }

    private PassModel.SynthesizedReturn? Synthesize(
        PassModel.Processor processor, LanguageModel.Production rule)
    {
        if (rule.Alternative is { IsNonTerminal: true } directField
            && this.source.Find(directField) is { } directSubtype)
        {
            int errors = this.problems.Count;
            var (registered, path) = this.SelectRegisteredInclusion(processor, directSubtype);
            if (this.problems.Count != errors)
            {
                return null;
            }

            var selected = registered;
            if (selected is null && (!processor.Result.IsTuple || processor.Defaults.Count > 0))
            {
                selected = this.SelectInclusion(
                    processor, directSubtype, processor.To, synthesize: true);
            }

            return selected is null ? null : InvokeInclusion(selected, path);
        }

        if (processor.Result.IsTuple && processor.Defaults.Count == 0)
        {
            return null;
        }

        string? shape = rule.Alternative is null ? PassGrammar.Shape(rule) : null;
        var matches = Candidates(processor.To, [], new(StringComparer.Ordinal))
            .Where(static candidate => candidate.Cost >= 0)
            .OrderBy(static candidate => candidate.Path.Length)
            .ThenBy(static candidate => candidate.Cost)
            .Take(2).ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length == 2 && matches[0].Path.Length == matches[1].Path.Length
            && matches[0].Cost == matches[1].Cost)
        {
            this.Invalid(
                processor.Method,
                $"has ambiguous structural matches for '{rule.Syntax}' in '{processor.To.Name}'.");
            return null;
        }

        var match = matches[0].Rule;
        var mapped = Fields(rule).Zip(Fields(match), (from, to) =>
            this.MapField(processor, rule, from, to)).ToArray();
        if (mapped.Any(static item => item is null))
        {
            return null;
        }

        var targetType = this.target.RuleType(match);
        if (targetType is null)
        {
            return null;
        }

        string expression = match.Alternative is null
            ? $"new {Type(targetType)}({string.Join(", ", mapped)})"
            : mapped[0]!;
        return new(WithDefaults(processor, Wrap(expression, matches[0].Path)));

        static string Wrap(string expression, ImmutableArray<PassModel.Kind> path)
        {
            foreach (var kind in path.Reverse())
            {
                expression = $"({Type(kind.Type)})({expression})";
            }

            return expression;
        }

        PassModel.SynthesizedReturn InvokeInclusion(
            PassModel.Processor selected, ImmutableArray<PassModel.Kind> path)
        {
            var args = Bind(selected, processor.Parameters)!.Value.Arguments;
            string call = this.InvokeFrom(processor, selected, NodeVariable, args);
            if (selected.Result.IsTuple)
            {
                string local = "__np_sub";
                while (processor.Method?.Parameters.Any(
                    parameter => string.Equals(parameter.Name, local, StringComparison.Ordinal))
                    == true)
                {
                    local += "_";
                }

                var primary = Wrap(local + ".Item1", path);
                var extras = Enumerable.Range(2, selected.Result.Arguments.Count - 1)
                    .Select(index => local + ".Item"
                        + index.ToString(CultureInfo.InvariantCulture));
                return new($"({primary}, {string.Join(", ", extras)})", (local, call));
            }

            return new(WithDefaults(processor, Wrap(call, path)));
        }

        IEnumerable<(LanguageModel.Production Rule, ImmutableArray<PassModel.Kind> Path, int Cost)>
            Candidates(
                PassModel.Kind kind, ImmutableArray<PassModel.Kind> path, HashSet<string> visited)
        {
            if (!visited.Add(kind.Name))
            {
                yield break;
            }

            path = path.Add(kind);
            foreach (var candidate in PassGrammar.Rules(kind))
            {
                bool matchesShape = shape is null
                    ? candidate.Alternative is { IsNonTerminal: false }
                    : candidate.Alternative is null && shape == PassGrammar.Shape(candidate);
                if (matchesShape)
                {
                    int cost = this.MatchFields(Fields(rule), Fields(candidate), processor);
                    yield return (candidate, path, cost);
                }

                if (candidate.Alternative is { IsNonTerminal: true } inclusion
                    && this.target.Find(inclusion) is { } nested)
                {
                    foreach (var match in Candidates(nested, path, visited))
                    {
                        yield return match;
                    }
                }
            }

            visited.Remove(kind.Name);
        }
    }

    private (PassModel.Processor? Processor, ImmutableArray<PassModel.Kind> Path)
        SelectRegisteredInclusion(PassModel.Processor processor, PassModel.Kind from)
    {
        int errors = this.problems.Count;
        var paths = new Queue<ImmutableArray<PassModel.Kind>>();
        var visited = new HashSet<PassModel.Kind> { processor.To };
        paths.Enqueue([processor.To]);
        PassModel.Processor? registered = null;
        ImmutableArray<PassModel.Kind> ancestors = [];
        while (paths.Count > 0)
        {
            var path = paths.Dequeue();
            if (registered is not null && path.Length > ancestors.Length + 1)
            {
                break;
            }

            var target = path[path.Length - 1];
            var selected = this.SelectInclusion(processor, from, target, synthesize: false);
            if (this.problems.Count != errors)
            {
                return (null, []);
            }

            if (selected is not null)
            {
                if (registered is not null)
                {
                    string message = $"has ambiguous processors for '{from.Name}' to "
                        + $"'{registered.To.Name}' or '{selected.To.Name}'.";
                    this.Invalid(processor.Method, message);
                    return (null, []);
                }

                registered = selected;
                ancestors = path.RemoveAt(path.Length - 1);
            }
            else if (registered is null)
            {
                foreach (var candidate in PassGrammar.Rules(target))
                {
                    if (candidate.Alternative is { } inclusion
                        && this.target.Find(inclusion) is { } nested && visited.Add(nested))
                    {
                        paths.Enqueue(path.Add(nested));
                    }
                }
            }
        }

        return (registered, ancestors);
    }

    private PassModel.Processor? SelectInclusion(
        PassModel.Processor processor, PassModel.Kind from, PassModel.Kind to, bool synthesize) =>
        from == processor.From && to == processor.To
            ? null
            : this.Select(
                from,
                to,
                processor.Parameters,
                processor.Method,
                synthesize,
                compatible: candidate => this.CompatibleExtraReturns(processor, candidate));

    private bool CompatibleExtraReturns(
        PassModel.Processor processor, PassModel.Processor candidate) =>
        candidate.Result.IsTuple
            ? processor.Result.IsTuple
                && candidate.Result.Arguments.Count == processor.Result.Arguments.Count
                && candidate.Result.Arguments.Skip(1).Zip(
                    processor.Result.Arguments.Skip(1), this.Converts).All(static match => match)
            : !processor.Result.IsTuple || processor.Defaults.Count > 0;

    private int MatchFields(
        IEnumerable<LanguageModel.Field> sourceFields,
        IEnumerable<LanguageModel.Field> targetFields,
        PassModel.Processor processor)
    {
        var sourceArray = sourceFields.ToArray();
        var targetArray = targetFields.ToArray();
        if (sourceArray.Length != targetArray.Length)
        {
            return -1;
        }

        int cost = 0;
        var pairs = sourceArray.Zip(targetArray, static (left, right) => (left, right));
        foreach (var (left, right) in pairs)
        {
            var from = this.source.Find(left);
            var to = this.target.Find(right);
            if (from is null || to is null || left.IsNonTerminal != right.IsNonTerminal
                || left.SequenceDepth != right.SequenceDepth || left.IsMaybe != right.IsMaybe)
            {
                return -1;
            }

            bool sameTarget = string.Equals(left.Target, right.Target, StringComparison.Ordinal);
            if (!sameTarget)
            {
                cost++;
            }

            bool requiresProcessor = !left.IsNonTerminal
                && (!sameTarget || !Same(from.Type, to.Type));
            if (requiresProcessor && this.Select(
                    from,
                    to,
                    processor.Parameters,
                    processor.Method,
                    synthesize: false,
                    report: false) is null)
            {
                return -1;
            }
        }

        return cost;
    }

    private string? MapField(
        PassModel.Processor processor,
        LanguageModel.Production rule,
        LanguageModel.Field sourceField,
        LanguageModel.Field targetField)
    {
        var from = this.source.Find(sourceField)!;
        var to = this.target.Find(targetField)!;
        int errors = this.problems.Count;
        var chosen = this.Select(
            from,
            to,
            processor.Parameters,
            processor.Method,
            synthesize: sourceField.IsNonTerminal);
        string expression = FieldExpression(rule, sourceField);
        if (chosen is null)
        {
            bool canCopy = this.problems.Count == errors
                && !sourceField.IsNonTerminal && from.Name == to.Name && Same(from.Type, to.Type);
            return canCopy ? expression : null;
        }

        var bound = Bind(chosen, processor.Parameters)!.Value.Arguments;
        return Map(
            sourceField,
            expression,
            Type(to.Type),
            value => Result(chosen, this.InvokeFrom(processor, chosen, value, bound)));
    }

    private void Missing(
        PassModel.Processor processor, LanguageModel.Production rule, PassType node)
    {
        string stem = "Rewrite" + ProductionReader.Identifier(
            rule.Alternative?.Target ?? rule.Name, "Production");
        string name = stem;
        for (int suffix = 2; !this.names.Add(name); suffix++)
        {
            name = stem + suffix.ToString(CultureInfo.InvariantCulture);
        }

        var owner = processor.Method is not null
            ? $", Processor = nameof({Id(processor.Name)})"
            : string.Empty;
        string parameter = "node";
        if (rule.Alternative is { IsNonTerminal: false } terminalField
            && this.source.Terminal(terminalField.Target) is { } terminal)
        {
            parameter = terminal.MetaVars.Append(terminal.Name)
                .FirstOrDefault(name => SyntaxFacts.IsValidIdentifier(Id(name))) ?? parameter;
        }

        var stub = $"[{Runtime}Rewrite(typeof({Type(node)}){owner})]\n"
            + $"private {Type(processor.Result)} {name}({Type(node)} {Id(parameter)}) => "
            + "throw new global::System.NotImplementedException();";
        string message = $"Pass '{input.Name}' needs a rewrite for '{rule.Syntax}' in language "
            + $"'{this.target.Language?.Name ?? "*"}', nonterminal '{processor.To.Name}'.";
        this.problems.Add(new(1, input.Location, message, stub));
    }
}
