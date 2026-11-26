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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using static Hcoona.Nanopass.Generators.PassInputReader;

namespace Hcoona.Nanopass.Generators;

internal sealed partial class PassResolver
{
    private readonly Dictionary<(PassModel.Kind From, PassModel.Kind To), bool>
        inferredExistentialMemo = [];

    private readonly Dictionary<(PassModel.Kind From, PassModel.Kind To), bool> inferredValidMemo =
        [];

    private static string Node(PassModel.Processor processor) =>
        Local(processor.Method?.Parameters ?? processor.Parameters, "__np_node");

    private static string FieldExpression(
        PassModel.Processor processor, LanguageModel.Production rule, LanguageModel.Field field) =>
        rule.Alternative is not null ? Node(processor) : $"{Node(processor)}.{Id(field.Property)}";

    private static HashSet<PassModel.Kind> ReachableTargetKinds(
        PassGrammar grammar, PassModel.Kind root)
    {
        var kinds = new HashSet<PassModel.Kind> { root };
        var queue = new Queue<PassModel.Kind>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            foreach (var candidate in grammar.Inclusions(queue.Dequeue()))
            {
                if (candidate.Alternative is { IsNonTerminal: true } inclusion
                    && grammar.Find(inclusion) is { } nested
                    && kinds.Add(nested))
                {
                    queue.Enqueue(nested);
                }
            }
        }

        return kinds;
    }

    private static EquatableArray<LanguageModel.Field> Fields(LanguageModel.Production? rule) =>
        rule?.Alternative is { } alternative ? new([alternative]) : rule?.Fields ?? default;

    private static void RelaxShortestInclusion(
        PassModel.Kind nested,
        PassModel.Kind current,
        int nextDepth,
        int currentPaths,
        Dictionary<PassModel.Kind, int> depthByKind,
        Dictionary<PassModel.Kind, int> pathCountByKind,
        Dictionary<PassModel.Kind, PassModel.Kind> parentByKind,
        Queue<PassModel.Kind> queue)
    {
        if (!depthByKind.TryGetValue(nested, out int bestDepth))
        {
            depthByKind[nested] = nextDepth;
            pathCountByKind[nested] = currentPaths;
            parentByKind[nested] = current;
            queue.Enqueue(nested);
        }
        else if (bestDepth == nextDepth)
        {
            pathCountByKind[nested] = Math.Min(2, pathCountByKind[nested] + currentPaths);
        }
    }

    private static ImmutableArray<PassModel.Kind> ReconstructAncestors(
        Dictionary<PassModel.Kind, PassModel.Kind> parentByKind, PassModel.Kind target)
    {
        if (!parentByKind.TryGetValue(target, out var current))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<PassModel.Kind>();
        while (true)
        {
            builder.Add(current);
            if (!parentByKind.TryGetValue(current, out current!))
            {
                break;
            }
        }

        builder.Reverse();
        return builder.ToImmutable();
    }

    private static PassModel.SynthesizedReturn WithDefaults(
        PassModel.Processor processor, string expression)
    {
        if (processor.Defaults.Count == 0)
        {
            return new(expression);
        }

        var defaults = string.Join(", ", processor.Defaults);
        return new($"({expression}, {defaults})", UsesDefaults: true);
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
        PassModel.Processor caller,
        PassModel.Processor callee,
        LanguageModel.Field field,
        string inputExpression,
        string returnType,
        ImmutableArray<string> arguments,
        Func<string, string, string> transform)
    {
        bool isStatic = arguments.IsEmpty;
        bool useState = isStatic && callee.Method?.IsStatic != true;
        var parameters = caller.Method?.Parameters ?? caller.Parameters;
        return MapLevel(field.SequenceDepth, inputExpression, "this");

        string MapLevel(int depth, string expression, string receiver)
        {
            string suffix = depth.ToString(CultureInfo.InvariantCulture);
            string item = Local(parameters, "__np_item" + suffix);
            if (depth > 0)
            {
                if (useState)
                {
                    string state = Local(parameters, "__np_state" + suffix);
                    return $"{expression}.Select({receiver}, static ({item}, {state}) => "
                        + $"{MapLevel(depth - 1, item, state)})";
                }

                string modifier = isStatic ? "static " : string.Empty;
                return $"{expression}.Select({modifier}{item} => "
                    + $"{MapLevel(depth - 1, item, receiver)})";
            }

            return field.IsMaybe
                ? $"({expression}) switch {{ {{ }} {item} => {transform(item, receiver)}, "
                    + $"_ => default({returnType}?) }}"
                : transform(expression, receiver);
        }
    }

    private static LanguageModel.Field? FindField(
        LanguageModel.Production? rule, string name, bool allowCamelCase = false)
    {
        var matches = Fields(rule).Where(field =>
            string.Equals(field.MetaVar, name, StringComparison.Ordinal)
            || string.Equals(field.Property, name, StringComparison.Ordinal)).Take(2).ToArray();
        if (matches.Length == 0 && allowCamelCase)
        {
            matches = Fields(rule).Where(field =>
                field.Property.Length > 0
                && string.Equals(
                    char.ToLowerInvariant(field.Property[0]) + field.Property.Substring(1),
                    name,
                    StringComparison.Ordinal)).Take(2).ToArray();
        }

        return matches.Length == 1 ? matches[0] : null;
    }

    private EquatableArray<PassModel.Alternative> Build(PassModel.Processor processor)
    {
        if (!processor.IsSynthesized)
        {
            return default;
        }

        var fallback = this.BuildClauses(processor, null);
        bool totalFallback = fallback.Any(static clause => clause.Guard is null);
        var rules = PassGrammar.Rules(processor.From).OrderByDescending(rule =>
            rule.Alternative is { IsNonTerminal: false } terminal
            && this.source.Terminal(terminal.Target)?.Predicate is not null).ToArray();
        var alternatives = new List<PassModel.Alternative>();
        string node = Node(processor);
        string value = Local(processor.Method?.Parameters ?? processor.Parameters, "__np_val");
        foreach (var rule in rules)
        {
            var clauses = this.BuildClauses(processor, rule);
            var terminal = rule.Alternative is { IsNonTerminal: false } field
                ? this.source.Terminal(field.Target)
                : null;
            var ruleType = this.source.RuleType(rule);
            bool overlaps = totalFallback && terminal?.Predicate is not null
                && this.Clauses(processor).Any(clause => clause.Rule is { } other && other != rule
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

            string condition = $"{value} is {Type(ruleType)} {node}";
            if (terminal is { Predicate: not null })
            {
                string binding = LanguageEmitter.TerminalBinding(terminal);
                condition += $" && {this.source.Language!.QualifiedName}.{binding}()"
                    + $".Accept({node})";
            }

            var rewrites = !totalClause && (!totalFallback || overlaps)
                ? clauses.AddRange(fallback)
                : clauses;
            alternatives.Add(new(condition, new(rewrites), last));
        }

        PassModel.SynthesizedReturn? identity = null;
        if (rules.Length == 0 && !totalFallback)
        {
            if (processor.From.Name == processor.To.Name
                && this.ConvertsPrimary(
                    processor.Method?.Parameters[0].Type ?? processor.From.Type,
                    processor.To,
                    processor.Result.Primary)
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
            alternatives.Add(new(null, new(fallback), identity));
        }

        return new(alternatives);
    }

    private ImmutableArray<(string? Guard, string Call)> BuildClauses(
        PassModel.Processor processor, LanguageModel.Production? rule)
    {
        var clauses = this.Clauses(processor).Where(clause => clause.Rule == rule)
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
                    : this.BindField(processor, rule, parameter.Name, parameter.Type);
                bool inputMatches = Same(processor.From.Type, parameter.Type)
                    && (rule is null || parameter.Name == "ir");
                expression ??= inputMatches
                    ? InputName(processor)
                    : nodeType is not null && Same(nodeType, parameter.Type)
                        ? Node(processor)
                        : this.BindField(
                            processor, rule, parameter.Name, parameter.Type, allowCamelCase: true);
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

    private string? BindField(
        PassModel.Processor processor,
        LanguageModel.Production? rule,
        string name,
        PassType type,
        bool allowCamelCase = false)
    {
        if (rule is not null && FindField(rule, name, allowCamelCase) is { } field
            && Same(
                this.source.Find(field)?.Type,
                ElementType(type, field.SequenceDepth, field.IsMaybe)))
        {
            return FieldExpression(processor, rule, field);
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
        var field = FindField(rule, fieldName, allowCamelCase: true);
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
            && candidate.From == from && Same(candidate.Result.Primary, result.Primary))
            .Select(candidate => (Processor: candidate, Binding: BindArguments(candidate),
                Named: this.target.MatchesTerminal(candidate.To.Name, parameter.Name),
                Cost: this.ExtraReturnCost(candidate.Result, result)))
            .Where(static candidate => candidate.Binding is not null && candidate.Cost >= 0)
            .OrderBy(static candidate => candidate.Cost)
            .ThenByDescending(static candidate => candidate.Binding!.Value.Matched)
            .ThenByDescending(static candidate => candidate.Named)
            .Take(2).ToArray();
        PassModel.Processor? chosen = candidates.FirstOrDefault().Processor;
        if (candidates.Length == 2
            && candidates[0].Cost == candidates[1].Cost
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
        string returnType = Type(result);
        return Map(
            processor,
            chosen,
            field,
            FieldExpression(processor, rule, field),
            returnType,
            bound,
            (value, receiver) =>
            {
                string call = this.InvokeFrom(processor, chosen, value, bound, receiver);
                return Same(chosen.Result, result) ? call : $"({returnType})({call})";
            });

        (ImmutableArray<string> Arguments, int Matched)? BindArguments(
            PassModel.Processor candidate) =>
            Bind(
                candidate,
                processor.Parameters,
                recur.Arguments,
                (name, type) => this.BindField(processor, rule, name, type));
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
                if (this.SelectMatchingTargetSubtype(processor, directSubtype) is
                    { } matchedSubtype)
                {
                    selected = this.SelectInclusion(
                        processor, directSubtype, matchedSubtype.Target, synthesize: true);
                    if (selected is not null)
                    {
                        path = matchedSubtype.Path;
                    }
                }

                selected ??= this.SelectInclusion(
                    processor, directSubtype, processor.To, synthesize: true);
            }

            return selected is null ? null : InvokeInclusion(selected, path);
        }

        if (processor.Result.IsTuple && processor.Defaults.Count == 0)
        {
            return null;
        }

        string? shape = this.source.Shape(rule);
        var (matches, parentByKind) = this.FindTargetMatches(processor, rule, shape);
        if (matches.Length == 0)
        {
            return null;
        }

        if (matches[0].PathCount > 1
            || (matches.Length == 2
                && matches[0].Depth == matches[1].Depth
                && matches[0].Cost == matches[1].Cost))
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

        ImmutableArray<PassModel.Kind> matchPath =
            [.. ReconstructAncestors(parentByKind, matches[0].Kind), matches[0].Kind];
        string expression = match.Alternative is null
            ? $"new {Type(targetType)}({string.Join(", ", match.Fields.Zip(
                mapped, static (to, field) => $"{Id(to.Property)}: {field}"))})"
            : mapped[0]!;
        return WithDefaults(processor, Wrap(expression, matchPath));

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
            string call = this.InvokeFrom(processor, selected, Node(processor), args);
            if (selected.Result.IsTuple)
            {
                string local = Local(
                    processor.Method?.Parameters ?? processor.Parameters, "__np_sub");

                var primary = Wrap(local + ".Item1", path);
                var extras = Enumerable.Range(2, selected.Result.Arguments.Count - 1)
                    .Select(index => local + ".Item"
                        + index.ToString(CultureInfo.InvariantCulture));
                return new($"({primary}, {string.Join(", ", extras)})", (local, call));
            }

            return WithDefaults(processor, Wrap(call, path));
        }
    }

    private (
        (LanguageModel.Production Rule, PassModel.Kind Kind, int Depth, int Cost, int PathCount)[]
            Matches,
        Dictionary<PassModel.Kind, PassModel.Kind> ParentByKind)
        FindTargetMatches(
            PassModel.Processor processor, LanguageModel.Production rule, string? shape)
    {
        var queue = new Queue<PassModel.Kind>();
        var depthByKind = new Dictionary<PassModel.Kind, int> { [processor.To] = 0 };
        var pathCountByKind = new Dictionary<PassModel.Kind, int> { [processor.To] = 1 };
        var parentByKind = new Dictionary<PassModel.Kind, PassModel.Kind>();
        var visitedOrder = new List<PassModel.Kind>();
        queue.Enqueue(processor.To);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visitedOrder.Add(current);
            int nextDepth = depthByKind[current] + 1;
            int currentPaths = pathCountByKind[current];
            foreach (var candidate in this.target.Inclusions(current))
            {
                if (candidate.Alternative is { IsNonTerminal: true } inclusion
                    && this.target.Find(inclusion) is { } nested)
                {
                    RelaxShortestInclusion(
                        nested,
                        current,
                        nextDepth,
                        currentPaths,
                        depthByKind,
                        pathCountByKind,
                        parentByKind,
                        queue);
                }
            }
        }

        var found = new List<(
            LanguageModel.Production Rule,
            PassModel.Kind Kind,
            int Depth,
            int Cost,
            int PathCount)>();
        var sourceFields = Fields(rule);
        foreach (var kind in visitedOrder)
        {
            int depth = depthByKind[kind];
            int paths = pathCountByKind[kind];
            foreach (var candidate in this.target.MatchingShape(kind, shape))
            {
                int cost = this.MatchFields(sourceFields, Fields(candidate), processor);
                if (cost >= 0)
                {
                    found.Add((candidate, kind, depth, cost, paths));
                }
            }
        }

        if (found.Count > 1)
        {
            var feasible = found
                .Where(item => this.AreNonTerminalFieldsFeasible(
                    processor, sourceFields, Fields(item.Rule)))
                .ToList();
            if (feasible.Count > 0)
            {
                found = feasible;
            }
        }

        var topMatches = found
            .OrderBy(static item => item.Depth)
            .ThenBy(static item => item.Cost)
            .Take(2)
            .ToArray();
        return (topMatches, parentByKind);
    }

    private (bool HandledByExplicit, bool Ambiguous) ProbeExplicitFieldProcessor(
        PassModel.Kind from,
        PassModel.Kind to,
        EquatableArray<PassInput.Parameter> ambient)
    {
        var candidates = this.processors
            .Where(p => p.Method is not null
                && !p.Result.IsTuple
                && p.From == from
                && p.To == to)
            .Select(p => Bind(p, ambient))
            .Where(static b => b is not null)
            .OrderByDescending(static b => b!.Value.Matched)
            .Take(2)
            .ToArray();
        if (candidates.Length == 0)
        {
            return (false, false);
        }

        return candidates.Length == 2
            && candidates[0]!.Value.Matched == candidates[1]!.Value.Matched
                ? (false, true)
                : (true, false);
    }

    private bool AreNonTerminalFieldsFeasible(
        PassModel.Processor processor,
        EquatableArray<LanguageModel.Field> sourceFields,
        EquatableArray<LanguageModel.Field> targetFields)
    {
        for (int i = 0; i < sourceFields.Count; i++)
        {
            if (!sourceFields[i].IsNonTerminal)
            {
                continue;
            }

            var fromFieldKind = this.source.Find(sourceFields[i])!;
            var toFieldKind = this.target.Find(targetFields[i])!;
            var (handledByExplicit, ambiguous) = this.ProbeExplicitFieldProcessor(
                fromFieldKind, toFieldKind, processor.Parameters);
            if (ambiguous
                || (!handledByExplicit
                    && !this.CanLowerExistentially(fromFieldKind, toFieldKind)))
            {
                return false;
            }
        }

        return true;
    }

    private (PassModel.Kind Target, ImmutableArray<PassModel.Kind> Path)?
        SelectMatchingTargetSubtype(PassModel.Processor processor, PassModel.Kind directSubtype)
    {
        var queue = new Queue<PassModel.Kind>();
        var depthByKind = new Dictionary<PassModel.Kind, int> { [processor.To] = 0 };
        var pathCountByKind = new Dictionary<PassModel.Kind, int> { [processor.To] = 1 };
        var parentByKind = new Dictionary<PassModel.Kind, PassModel.Kind>();
        queue.Enqueue(processor.To);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int depth = depthByKind[current];
            int currentPaths = pathCountByKind[current];
            if (depth > 0
                && string.Equals(current.Name, directSubtype.Name, StringComparison.Ordinal))
            {
                return currentPaths == 1
                    && this.CanLowerWithinSubtype(processor, directSubtype, current)
                        ? (current, ReconstructAncestors(parentByKind, current))
                        : null;
            }

            int nextDepth = depth + 1;
            foreach (var candidate in this.target.Inclusions(current))
            {
                if (candidate.Alternative is { IsNonTerminal: true } inclusion
                    && this.target.Find(inclusion) is { } nested)
                {
                    RelaxShortestInclusion(
                        nested,
                        current,
                        nextDepth,
                        currentPaths,
                        depthByKind,
                        pathCountByKind,
                        parentByKind,
                        queue);
                }
            }
        }

        return null;
    }

    private bool CanLowerWithinSubtype(
        PassModel.Processor processor,
        PassModel.Kind sourceSubtype,
        PassModel.Kind targetSubtype) =>
        !this.HasOuterRegisteredInclusion(sourceSubtype, targetSubtype, processor.To)
        && (this.CanLowerInferred(sourceSubtype, targetSubtype)
            || !this.CanLowerInferred(sourceSubtype, processor.To));

    private bool HasOuterRegisteredInclusion(
        PassModel.Kind sourceRoot,
        PassModel.Kind innerTarget,
        PassModel.Kind outerTarget)
    {
        var innerCone = ReachableTargetKinds(this.target, innerTarget);
        var outerCone = ReachableTargetKinds(this.target, outerTarget);
        var pending = new Queue<PassModel.Kind>();
        var visited = new HashSet<PassModel.Kind> { sourceRoot };
        pending.Enqueue(sourceRoot);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (this.processors.Any(c =>
                (c.Method is not null || this.Clauses(c).Count > 0)
                && !(current == sourceRoot && c.To == innerTarget)
                && !c.Result.IsTuple
                && c.From == current
                && innerCone.Contains(c.To)
                && Bind(c, default) is not null))
            {
                continue;
            }

            if (this.processors.Any(c =>
                (c.Method is not null || this.Clauses(c).Count > 0)
                && !(current == sourceRoot && c.To == outerTarget)
                && !c.Result.IsTuple
                && c.From == current
                && outerCone.Contains(c.To)
                && !innerCone.Contains(c.To)
                && Bind(c, default) is not null))
            {
                return true;
            }

            var anon = current == sourceRoot
                ? this.processors.FirstOrDefault(p =>
                    p.Method is null && p.From == sourceRoot && p.To == innerTarget)
                : null;
            if (anon is not null
                && this.Clauses(anon).Any(c => c.Rule is null && c.Rewrite.Guard is null))
            {
                continue;
            }

            foreach (var rule in PassGrammar.Rules(current))
            {
                if (rule.Alternative is { IsNonTerminal: true } inclusion
                    && (anon is null || !this.Clauses(anon).Any(c =>
                        c.Rule == rule && c.Rewrite.Guard is null))
                    && this.source.Find(inclusion) is { } nested
                    && visited.Add(nested))
                {
                    pending.Enqueue(nested);
                }
            }
        }

        return false;
    }

    private bool CanLowerExistentially(PassModel.Kind sourceRoot, PassModel.Kind targetRoot)
    {
        this.EnsureInferredClosure((sourceRoot, targetRoot));
        return this.inferredExistentialMemo[(sourceRoot, targetRoot)];
    }

    private bool CanLowerInferred(PassModel.Kind sourceRoot, PassModel.Kind targetRoot)
    {
        this.EnsureInferredClosure((sourceRoot, targetRoot));
        return this.inferredValidMemo[(sourceRoot, targetRoot)];
    }

    private void EnsureInferredClosure((PassModel.Kind From, PassModel.Kind To) rootPair)
    {
        if (this.inferredValidMemo.ContainsKey(rootPair))
        {
            return;
        }

        var specs =
            new Dictionary<(PassModel.Kind From, PassModel.Kind To), InferredRuleSpec[]>();
        var discoveryQueue = new Queue<(PassModel.Kind From, PassModel.Kind To)>();
        discoveryQueue.Enqueue(rootPair);
        while (discoveryQueue.Count > 0)
        {
            var pair = discoveryQueue.Dequeue();
            if (this.inferredValidMemo.ContainsKey(pair) || specs.ContainsKey(pair))
            {
                continue;
            }

            var rules = this.BuildInferredRules(pair.From, pair.To);
            if (rules is null)
            {
                this.inferredExistentialMemo[pair] = false;
                this.inferredValidMemo[pair] = false;
                continue;
            }

            specs[pair] = rules;
            foreach (var ruleSpec in rules)
            {
                foreach (var candidate in ruleSpec.Candidates)
                {
                    foreach (var dep in candidate.Deps)
                    {
                        if (!this.inferredValidMemo.ContainsKey(dep) && !specs.ContainsKey(dep))
                        {
                            discoveryQueue.Enqueue(dep);
                        }
                    }
                }
            }
        }

        if (specs.Count == 0)
        {
            return;
        }

        var existActive = new HashSet<(PassModel.Kind From, PassModel.Kind To)>(specs.Keys);
        bool IsExistential((PassModel.Kind From, PassModel.Kind To) q) =>
            existActive.Contains(q)
            || (this.inferredExistentialMemo.TryGetValue(q, out bool ok) && ok);

        bool shrank = true;
        while (shrank)
        {
            shrank = false;
            foreach (var kvp in specs)
            {
                if (existActive.Contains(kvp.Key)
                    && !kvp.Value.All(r => r.Candidates.Any(c => c.Deps.All(IsExistential))))
                {
                    existActive.Remove(kvp.Key);
                    shrank = true;
                }
            }
        }

        foreach (var p in specs.Keys)
        {
            this.inferredExistentialMemo[p] = existActive.Contains(p);
        }

        var ruleDisjuncts = new Dictionary<
            (PassModel.Kind From, PassModel.Kind To),
            (PassModel.Kind From, PassModel.Kind To)[][][]>();
        foreach (var p in existActive)
        {
            var perRule = new List<(PassModel.Kind From, PassModel.Kind To)[][]>();
            bool allRulesFeasible = true;
            foreach (var r in specs[p])
            {
                if (r.IsInclusionFallback)
                {
                    var validOptions = r.Candidates
                        .Where(c => c.Deps.All(IsExistential))
                        .Select(static c => c.Deps)
                        .ToArray();
                    if (validOptions.Length == 0)
                    {
                        allRulesFeasible = false;
                        break;
                    }

                    perRule.Add(validOptions);
                    continue;
                }

                var top = r.Candidates.Where(c => c.Deps.All(IsExistential)).Take(2).ToArray();
                if (top.Length == 0
                    || top[0].TerminalFailed
                    || top[0].PathCount != 1
                    || (top.Length == 2
                        && top[0].Depth == top[1].Depth
                        && top[0].Cost == top[1].Cost))
                {
                    allRulesFeasible = false;
                    break;
                }

                if (top[0].Deps.Length > 0)
                {
                    perRule.Add([top[0].Deps]);
                }
            }

            if (allRulesFeasible)
            {
                ruleDisjuncts[p] = perRule.ToArray();
            }
        }

        var validActive = new HashSet<(PassModel.Kind From, PassModel.Kind To)>(ruleDisjuncts.Keys);
        bool IsValid((PassModel.Kind From, PassModel.Kind To) q) =>
            validActive.Contains(q)
            || (this.inferredValidMemo.TryGetValue(q, out bool ok) && ok);

        shrank = true;
        while (shrank)
        {
            shrank = false;
            foreach (var kvp in ruleDisjuncts)
            {
                if (validActive.Contains(kvp.Key)
                    && !kvp.Value.All(options => options.Any(deps => deps.All(IsValid))))
                {
                    validActive.Remove(kvp.Key);
                    shrank = true;
                }
            }
        }

        foreach (var p in specs.Keys)
        {
            this.inferredValidMemo[p] = validActive.Contains(p);
        }
    }

    private InferredRuleSpec[]? BuildInferredRules(PassModel.Kind s, PassModel.Kind t)
    {
        var anon = this.processors.FirstOrDefault(p =>
            p.Method is null && p.From == s && p.To == t);
        if (anon is not null
            && this.Clauses(anon).Any(c => c.Rule is null && c.Rewrite.Guard is null))
        {
            return [];
        }

        var rules = PassGrammar.Rules(s);
        if (rules.Length == 0)
        {
            return s.Name == t.Name && this.ConvertsPrimary(s.Type, t, t.Type) ? [] : null;
        }

        var probe = new PassModel.Processor(string.Empty, s, t, t.Type, default, null, default);
        var builtRules = new List<InferredRuleSpec>();
        foreach (var rule in rules)
        {
            if (anon is not null
                && this.Clauses(anon).Any(c => c.Rule == rule && c.Rewrite.Guard is null))
            {
                continue;
            }

            if (this.source.RuleType(rule) is null)
            {
                return null;
            }

            if (rule.Alternative is { IsNonTerminal: true } directField
                && this.source.Find(directField) is { } d)
            {
                var incSpec = this.BuildInclusionRuleCandidates(s, t, d);
                if (incSpec is null)
                {
                    return null;
                }

                if (incSpec.Value.Candidates.Length > 0)
                {
                    builtRules.Add(incSpec.Value);
                }

                continue;
            }

            var prodCandidates = this.BuildProductionRuleCandidates(probe, t, rule);
            if (prodCandidates is null)
            {
                return null;
            }

            builtRules.Add(new InferredRuleSpec(false, prodCandidates));
        }

        return builtRules.ToArray();
    }

    private InferredRuleSpec? BuildInclusionRuleCandidates(
        PassModel.Kind s, PassModel.Kind t, PassModel.Kind d)
    {
        var queue = new Queue<PassModel.Kind>();
        var depthByKind = new Dictionary<PassModel.Kind, int> { [t] = 0 };
        var pathCountByKind = new Dictionary<PassModel.Kind, int> { [t] = 1 };
        var parentByKind = new Dictionary<PassModel.Kind, PassModel.Kind>();
        queue.Enqueue(t);
        PassModel.Processor? registered = null;
        int matchedDepth = -1;
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int depth = depthByKind[cur];
            if (registered is not null && depth > matchedDepth)
            {
                break;
            }

            var matches = d == s && cur == t
                ? []
                : this.processors
                    .Where(p => (p.Method is not null || this.Clauses(p).Count > 0)
                        && !p.Result.IsTuple
                        && p.From == d
                        && p.To == cur)
                    .Select(p => (Processor: p, Binding: Bind(p, default)))
                    .Where(static item => item.Binding is not null)
                    .OrderByDescending(static item => item.Binding!.Value.Matched)
                    .Take(2)
                    .ToArray();
            if (matches.Length == 2
                && matches[0].Binding!.Value.Matched == matches[1].Binding!.Value.Matched)
            {
                return null;
            }

            int currentPaths = pathCountByKind[cur];
            if (matches.Length > 0)
            {
                if (registered is not null || currentPaths > 1)
                {
                    return null;
                }

                registered = matches[0].Processor;
                matchedDepth = depth;
            }
            else if (registered is null)
            {
                int nextDepth = depth + 1;
                foreach (var candidate in PassGrammar.Rules(cur))
                {
                    if (candidate.Alternative is { } inc && this.target.Find(inc) is { } nested)
                    {
                        RelaxShortestInclusion(
                            nested,
                            cur,
                            nextDepth,
                            currentPaths,
                            depthByKind,
                            pathCountByKind,
                            parentByKind,
                            queue);
                    }
                }
            }
        }

        if (registered is not null)
        {
            return registered.Method is not null
                ? new InferredRuleSpec(false, [])
                : new InferredRuleSpec(
                    false, [new InferredCandidate(0, 0, 1, false, [(d, registered.To)])]);
        }

        var matchedSubtype = this.ProbeUniqueNamedTargetSubtype(t, d);
        var options = new List<InferredCandidate>(2);
        if (matchedSubtype is not null
            && !(d == s && matchedSubtype == t)
            && !this.HasOuterRegisteredInclusion(d, matchedSubtype, t))
        {
            options.Add(new InferredCandidate(0, 0, 1, false, [(d, matchedSubtype)]));
        }

        if (d != s)
        {
            options.Add(new InferredCandidate(1, 0, 1, false, [(d, t)]));
        }

        return options.Count == 0
            ? null
            : new InferredRuleSpec(true, options.ToArray());
    }

    private PassModel.Kind? ProbeUniqueNamedTargetSubtype(
        PassModel.Kind rootTarget, PassModel.Kind directSubtype)
    {
        var queue = new Queue<PassModel.Kind>();
        var depthByKind = new Dictionary<PassModel.Kind, int> { [rootTarget] = 0 };
        var pathCountByKind = new Dictionary<PassModel.Kind, int> { [rootTarget] = 1 };
        var parentByKind = new Dictionary<PassModel.Kind, PassModel.Kind>();
        queue.Enqueue(rootTarget);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int depth = depthByKind[current];
            if (depth > 0
                && string.Equals(current.Name, directSubtype.Name, StringComparison.Ordinal))
            {
                return pathCountByKind[current] == 1 ? current : null;
            }

            int nextDepth = depth + 1;
            int currentPaths = pathCountByKind[current];
            foreach (var candidate in this.target.Inclusions(current))
            {
                if (candidate.Alternative is { IsNonTerminal: true } inc
                    && this.target.Find(inc) is { } nested)
                {
                    RelaxShortestInclusion(
                        nested,
                        current,
                        nextDepth,
                        currentPaths,
                        depthByKind,
                        pathCountByKind,
                        parentByKind,
                        queue);
                }
            }
        }

        return null;
    }

    private InferredCandidate[]? BuildProductionRuleCandidates(
        PassModel.Processor probe, PassModel.Kind t, LanguageModel.Production rule)
    {
        var queue = new Queue<PassModel.Kind>();
        var depthByKind = new Dictionary<PassModel.Kind, int> { [t] = 0 };
        var pathCountByKind = new Dictionary<PassModel.Kind, int> { [t] = 1 };
        var parentByKind = new Dictionary<PassModel.Kind, PassModel.Kind>();
        var visitedOrder = new List<PassModel.Kind>();
        queue.Enqueue(t);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visitedOrder.Add(current);
            int nextDepth = depthByKind[current] + 1;
            int currentPaths = pathCountByKind[current];
            foreach (var incRule in this.target.Inclusions(current))
            {
                if (incRule.Alternative is { IsNonTerminal: true } inc
                    && this.target.Find(inc) is { } nested)
                {
                    RelaxShortestInclusion(
                        nested,
                        current,
                        nextDepth,
                        currentPaths,
                        depthByKind,
                        pathCountByKind,
                        parentByKind,
                        queue);
                }
            }
        }

        string? shape = this.source.Shape(rule);
        var sourceFields = Fields(rule);
        var candidates = new List<InferredCandidate>();
        foreach (var kind in visitedOrder)
        {
            int depth = depthByKind[kind];
            int paths = pathCountByKind[kind];
            foreach (var candidate in this.target.MatchingShape(kind, shape))
            {
                if (this.target.RuleType(candidate) is null)
                {
                    continue;
                }

                var targetFields = Fields(candidate);
                int cost = this.MatchFields(sourceFields, targetFields, probe);
                if (cost < 0)
                {
                    continue;
                }

                bool terminalFailed = false;
                bool impossibleField = false;
                var deps = new List<(PassModel.Kind From, PassModel.Kind To)>();
                for (int i = 0; i < sourceFields.Count; i++)
                {
                    var fromF = this.source.Find(sourceFields[i])!;
                    var toF = this.target.Find(targetFields[i])!;
                    var (handled, ambig) = this.ProbeExplicitFieldProcessor(fromF, toF, default);
                    if (!sourceFields[i].IsNonTerminal)
                    {
                        terminalFailed |= ambig;
                        continue;
                    }

                    if (ambig)
                    {
                        impossibleField = true;
                        break;
                    }

                    if (!handled)
                    {
                        deps.Add((fromF, toF));
                    }
                }

                if (!impossibleField)
                {
                    candidates.Add(
                        new InferredCandidate(depth, cost, paths, terminalFailed, deps.ToArray()));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(static item => item.Depth)
            .ThenBy(static item => item.Cost)
            .ToArray();
    }

    private (PassModel.Processor? Processor, ImmutableArray<PassModel.Kind> Path)
        SelectRegisteredInclusion(PassModel.Processor processor, PassModel.Kind from)
    {
        int errors = this.problems.Count;
        var queue = new Queue<PassModel.Kind>();
        var depthByKind = new Dictionary<PassModel.Kind, int> { [processor.To] = 0 };
        var pathCountByKind = new Dictionary<PassModel.Kind, int> { [processor.To] = 1 };
        var parentByKind = new Dictionary<PassModel.Kind, PassModel.Kind>();
        queue.Enqueue(processor.To);
        PassModel.Processor? registered = null;
        int matchedDepth = -1;
        ImmutableArray<PassModel.Kind> ancestors = [];
        while (queue.Count > 0)
        {
            var target = queue.Dequeue();
            int depth = depthByKind[target];
            if (registered is not null && depth > matchedDepth)
            {
                break;
            }

            var selected = this.SelectInclusion(processor, from, target, synthesize: false);
            if (this.problems.Count != errors)
            {
                return (null, []);
            }

            int currentPaths = pathCountByKind[target];
            if (selected is not null)
            {
                if (registered is not null)
                {
                    string message = $"has ambiguous processors for '{from.Name}' to "
                        + $"'{registered.To.Name}' or '{selected.To.Name}'.";
                    this.Invalid(processor.Method, message);
                    return (null, []);
                }

                if (currentPaths > 1)
                {
                    string message = $"has ambiguous subtype paths from '{selected.To.Name}' to "
                        + $"'{processor.To.Name}'.";
                    this.Invalid(processor.Method, message);
                    return (null, []);
                }

                registered = selected;
                matchedDepth = depth;
                ancestors = ReconstructAncestors(parentByKind, target);
            }
            else if (registered is null)
            {
                int nextDepth = depth + 1;
                foreach (var candidate in PassGrammar.Rules(target))
                {
                    if (candidate.Alternative is { } inclusion
                        && this.target.Find(inclusion) is { } nested)
                    {
                        RelaxShortestInclusion(
                            nested,
                            target,
                            nextDepth,
                            currentPaths,
                            depthByKind,
                            pathCountByKind,
                            parentByKind,
                            queue);
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
                compatibilityCost: candidate =>
                    !candidate.Result.IsTuple && processor.Defaults.Count > 0
                        ? int.MaxValue
                        : this.ExtraReturnCost(candidate.Result, processor.Result));

    private int ExtraReturnCost(
        PassType actual,
        PassType expected,
        int firstSlot = 1,
        SlotSearch? search = null,
        int varianceDepth = 0)
    {
        if (actual.IsTuple != expected.IsTuple)
        {
            return -1;
        }

        if (!actual.IsTuple)
        {
            return 0;
        }

        if (actual.Arguments.Count != expected.Arguments.Count)
        {
            return -1;
        }

        int cost = 0;
        for (int i = firstSlot; i < actual.Arguments.Count; i++)
        {
            int slotCost = this.SlotCost(
                actual.Arguments[i],
                expected.Arguments[i],
                search: search,
                varianceDepth: varianceDepth);
            if (slotCost < 0)
            {
                return -1;
            }

            cost += slotCost;
        }

        return cost;
    }

    private int SlotCost(
        PassType fromType,
        PassType to,
        bool referenceOnly = false,
        SlotSearch? search = null,
        int varianceDepth = 0)
    {
        search ??= new(
            Math.Max(1024, Math.Max(fromType.NodeCount, to.NodeCount)),
            Math.Max(4096, (fromType.NodeCount + to.NodeCount) * 4));
        if (fromType.NodeCount > search.MaxNodes || to.NodeCount > search.MaxNodes)
        {
            return -1;
        }

        var pair = (fromType.Key, to.Key, referenceOnly, varianceDepth);
        if (search.Memo.TryGetValue(pair, out int cached))
        {
            return cached;
        }

        if (search.Budget <= 0)
        {
            return -1;
        }

        search.Budget--;
        search.Memo[pair] = -1;
        int result = MatchSlot();
        search.Memo[pair] = result;
        return result;

        int MatchSlot()
        {
            if (referenceOnly && !IsReferenceType(fromType))
            {
                return -1;
            }

            if (Same(fromType, to))
            {
                return 0;
            }

            if (!fromType.Nullable && to.Nullable
                && this.SlotCost(
                    fromType, to with { Nullable = false }, referenceOnly, search, varianceDepth)
                    is >= 0 and var lifted)
            {
                return lifted + 1;
            }

            if (fromType.HasTupleElements && to.HasTupleElements)
            {
                if (fromType.Nullable != to.Nullable
                    || fromType.Arguments.Count != to.Arguments.Count)
                {
                    return -1;
                }

                int nestedCost = this.ExtraReturnCost(
                    fromType with { Nullable = false },
                    to with { Nullable = false },
                    firstSlot: 0,
                    search: search,
                    varianceDepth: varianceDepth);
                return nestedCost < 0 ? -1 : Math.Max(1, nestedCost);
            }

            if (fromType.Nullable && !to.Nullable)
            {
                return -1;
            }

            var fromShape = fromType with { Nullable = false };
            var toShape = to with { Nullable = false };
            bool sameArrayRank = fromShape.ArrayRank > 0
                && fromShape.ArrayRank == toShape.ArrayRank;
            bool szArrayInterface = varianceDepth < 50
                && fromShape.ArrayRank == 1 && IsSzArrayInterface(toShape);
            if (sameArrayRank || szArrayInterface)
            {
                var element = fromType.Arguments[0];
                var target = to.Arguments[0];
                int nextDepth = szArrayInterface ? varianceDepth + 1 : varianceDepth;
                bool compatible = Same(element, target)
                    || this.SlotCost(
                        element,
                        target,
                        referenceOnly: true,
                        search: search,
                        varianceDepth: nextDepth) >= 0;
                return compatible ? 1 : -1;
            }

            if ((!referenceOnly && !fromType.Nullable
                    && (this.source.CanConvert(fromType, to)
                        || this.target.CanConvert(fromType, to)))
                || to.Name is "object" or "global::System.Object")
            {
                return 1;
            }

            foreach (var projection in input.Projections)
            {
                var projected = projection.AsTarget;
                if (!Same(fromShape, projection.From)
                    || projected.Name != toShape.Name
                    || !Same(projected.ContainingType, toShape.ContainingType)
                    || projection.Variances.Count != toShape.Arguments.Count)
                {
                    continue;
                }

                bool compatible = true;
                for (int i = 0; i < projection.Variances.Count; i++)
                {
                    var actual = projected.Arguments[i];
                    var expected = toShape.Arguments[i];
                    if (!Same(actual, expected)
                        && (varianceDepth >= 50 || projection.Variances[i] switch
                        {
                            VarianceKind.Out => this.SlotCost(
                                actual,
                                expected,
                                referenceOnly: true,
                                search: search,
                                varianceDepth: varianceDepth + 1) < 0,
                            VarianceKind.In => this.SlotCost(
                                expected,
                                actual,
                                referenceOnly: true,
                                search: search,
                                varianceDepth: varianceDepth + 1) < 0,
                            _ => true,
                        }))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible)
                {
                    return 1;
                }
            }

            return !this.IsGrammarUnion(to)
                && this.HasImplicitConversion(fromType, to, referenceOnly)
                    ? 1
                    : -1;
        }

        bool IsReferenceType(PassType type)
        {
            var unannotated = type with { Nullable = false };
            return input.ReferenceTypes.Any(known => Same(known, unannotated))
                || ContainsRecord(this.source) || ContainsRecord(this.target);

            bool ContainsRecord(PassGrammar grammar) => grammar.Kinds.Any(kind =>
                PassGrammar.Rules(kind).Any(rule => rule.Alternative is null
                    && Same(unannotated, grammar.RuleType(rule))));
        }
    }

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
                    report: false,
                    compatibilityCost: static c => c.Result.IsTuple ? -1 : 0) is null)
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
            synthesize: sourceField.IsNonTerminal,
            compatibilityCost: static candidate => candidate.Result.IsTuple ? -1 : 0);
        string expression = FieldExpression(processor, rule, sourceField);
        if (chosen is null)
        {
            bool canCopy = this.problems.Count == errors
                && !sourceField.IsNonTerminal && from.Name == to.Name && Same(from.Type, to.Type);
            return canCopy ? expression : null;
        }

        var bound = Bind(chosen, processor.Parameters)!.Value.Arguments;
        return Map(
            processor,
            chosen,
            sourceField,
            expression,
            Type(to.Type),
            bound,
            (value, receiver) => this.InvokeFrom(processor, chosen, value, bound, receiver));
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
                .FirstOrDefault(name => SyntaxFacts.IsValidIdentifier(Id(name))
                    && !processor.Parameters.Any(ambient => ambient.Name == name)) ?? parameter;
        }

        parameter = Local(processor.Parameters, parameter);
        var stub = $"[{Runtime}Rewrite(typeof({Type(node)}){owner})]\n"
            + $"private {Type(processor.Result)} {name}({Type(node)} {Id(parameter)}) => "
            + "throw new global::System.NotImplementedException();";
        string message = $"Pass '{input.Name}' needs a rewrite for '{rule.Syntax}' in language "
            + $"'{this.target.Language?.Name ?? "*"}', nonterminal '{processor.To.Name}'.";
        this.problems.Add(new(1, input.Location, message, stub));
    }

    private readonly struct InferredRuleSpec(
        bool isInclusionFallback,
        InferredCandidate[] candidates)
    {
        internal bool IsInclusionFallback { get; } = isInclusionFallback;

        internal InferredCandidate[] Candidates { get; } = candidates;
    }

    private readonly struct InferredCandidate(
        int depth,
        int cost,
        int pathCount,
        bool terminalFailed,
        (PassModel.Kind From, PassModel.Kind To)[] deps)
    {
        internal int Depth { get; } = depth;

        internal int Cost { get; } = cost;

        internal int PathCount { get; } = pathCount;

        internal bool TerminalFailed { get; } = terminalFailed;

        internal (PassModel.Kind From, PassModel.Kind To)[] Deps { get; } = deps;
    }

    private sealed class SlotSearch(int maxNodes, int budget)
    {
        internal int MaxNodes { get; } = maxNodes;

        internal int Budget { get; set; } = budget;

        internal Dictionary<(string From, string To, bool ReferenceOnly, int Depth), int> Memo
        { get; } = [];
    }
}
