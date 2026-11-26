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

using static Hcoona.Nanopass.Generators.PassInputReader;

namespace Hcoona.Nanopass.Generators;

internal sealed partial class PassResolver(
    PassInput input, LanguageModel? sourceLanguage, LanguageModel? targetLanguage)
{
    internal const string InputVariable = "__np_input";
    internal const string NodeVariable = "__np_node";

    private readonly PassGrammar source = new(input.From, sourceLanguage);
    private readonly PassGrammar target = new(input.To, targetLanguage);
    private readonly List<PassModel.Processor> processors = [];
    private readonly Dictionary<PassModel.Processor, HashSet<PassModel.Processor>> calls = [];
    private readonly List<PassModel.Problem> problems = [];
    private readonly HashSet<string> names = new(
        input.MemberNames, StringComparer.Ordinal);

    internal static string Local(EquatableArray<PassInput.Parameter> parameters, string name)
    {
        while (parameters.Any(parameter => parameter.Name == name))
        {
            name += "_";
        }

        return name;
    }

    internal PassModel Resolve()
    {
        var sourceEntry = this.Entry(this.source);
        var targetEntry = this.Entry(this.target);
        var partialRuns = input.Methods.Where(static method =>
            method.Name == "Run" && method.IsPartialDeclaration).ToArray();
        var customRuns = input.Methods.Where(method =>
            method.Name == "Run" && !method.IsPartialDeclaration
            && method.Parameters.Count > 0 && Same(method.Parameters[0].Type, sourceEntry?.Type))
            .OrderBy(static method => method.Parameters.Count).ToArray();
        var run = partialRuns.FirstOrDefault() ?? customRuns.FirstOrDefault();
        bool customRun = run is { IsPartialDeclaration: false };
        var parameters = run is null ? [] : Parameters(run.Parameters.Skip(1));
        if (partialRuns.Length > 1 || (run is not null && sourceEntry is not null
            && targetEntry is not null && (!ValidMethod(run)
                || !Same(run.Parameters[0].Type, sourceEntry.Type)
                || !this.ValidRunResult(run, targetEntry.Type))))
        {
            this.Invalid(run, "has an invalid or ambiguous Run declaration.");
        }

        foreach (var method in input.Methods)
        {
            if (method.Processor is { } processor)
            {
                this.ReadProcessor(method, processor);
            }
        }

        string? entryCall = null;
        string? interfaceCall = null;
        if (sourceEntry is not null && targetEntry is not null && this.problems.Count == 0)
        {
            bool needsEntry = !customRun || input.Methods.Any(static method =>
                method.Rewrites.Any(static rewrite => rewrite.Processor is null));
            var entry = needsEntry
                ? this.Select(
                    sourceEntry,
                    targetEntry,
                    parameters,
                    run,
                    synthesize: !customRun,
                    report: !customRun)
                : null;
            if (entry is null && needsEntry && !customRun && this.problems.Count == 0)
            {
                this.Invalid(run, "has no applicable entry processor.");
            }

            if (entry is not null)
            {
                var ir = run is null ? InputVariable : Id(run.Parameters[0].Name);
                var bound = Bind(entry, parameters)!.Value.Arguments;
                entryCall = Result(entry, Invoke(entry, ir, bound));
                interfaceCall = run is null ? entryCall : null;
            }

            if (run is not null && parameters.All(static parameter => parameter.CanOmit))
            {
                var omitted = parameters
                    .Select(static parameter => OmittedArgument(parameter))
                    .OfType<string>();
                interfaceCall = Call(run, [InputVariable, .. omitted]);
            }

            foreach (var method in input.Methods)
            {
                foreach (var rewrite in method.Rewrites)
                {
                    this.ReadRewrite(method, rewrite, entry);
                }
            }

            if (this.problems.Count == 0)
            {
                for (int i = 0; i < this.processors.Count; i++)
                {
                    this.Build(this.processors[i]);
                }

                this.CheckReachability(customRun ? null : entry);
            }
        }

        var diagnostics = this.problems.Any(static problem => problem.Number != 1)
            ? this.problems.Where(static problem => problem.Number != 1)
            : this.problems;
        return new(
            input,
            this.source.Language,
            this.target.Language,
            sourceEntry,
            targetEntry,
            run,
            entryCall,
            interfaceCall,
            [.. this.processors],
            [.. diagnostics]);
    }

    private static bool ValidMethod(PassInput.Method method) =>
        method.TypeParameters.Count == 0 && method.ReturnType.Name != "void"
        && method.ReturnModifier.Length == 0 && method.Parameters.Count > 0
        && method.Parameters.All(static parameter => IsValueParameter(parameter));

    private static string Result(PassModel.Processor processor, string expression) =>
        processor.Result.IsTuple ? expression + ".Item1" : expression;

    private static bool IsValueParameter(PassInput.Parameter parameter) =>
        parameter.Modifier is "" or "params ";

    private static ImmutableArray<PassInput.Parameter> Parameters(
        IEnumerable<PassInput.Parameter> parameters) => [.. parameters];

    private static string InputName(PassModel.Processor processor) =>
        processor.Method is { } method ? Id(method.Parameters[0].Name) : InputVariable;

    private static string Call(PassInput.Method method, IEnumerable<string> arguments) =>
        (method.IsStatic ? string.Empty : "this.") + Id(method.Name)
        + "(" + string.Join(", ", arguments) + ")";

    private static string Invoke(
        PassModel.Processor processor, string ir, ImmutableArray<string> arguments) =>
        processor.Method is { } method
            ? Call(method, [ir, .. arguments])
            : $"this.{Id(processor.Name)}({string.Join(", ", new[] { ir }.Concat(arguments))})";

    private static (ImmutableArray<string> Arguments, int Matched)? Bind(
        PassModel.Processor processor,
        ImmutableArray<PassInput.Parameter> ambient,
        EquatableArray<string> explicitArguments = default,
        Func<string, PassType, string?>? field = null)
    {
        if (explicitArguments.Count > processor.Parameters.Length)
        {
            return null;
        }

        var result = ImmutableArray.CreateBuilder<string>();
        int matched = 0;
        for (int i = 0; i < processor.Parameters.Length; i++)
        {
            var parameter = processor.Parameters[i];
            bool explicitName = i < explicitArguments.Count;
            string name = explicitName ? explicitArguments[i] : parameter.Name;
            var argument = ambient.FirstOrDefault(value =>
                string.Equals(value.Name, name, StringComparison.Ordinal)
                && Same(value.Type, parameter.Type));
            var expression = argument is not null
                ? Id(argument.Name)
                : explicitName ? field?.Invoke(name, parameter.Type) : null;
            if (expression is not null)
            {
                result.Add($"{Id(parameter.Name)}: {expression}");
                matched++;
            }
            else if (!parameter.CanOmit || explicitName)
            {
                return null;
            }
            else if (OmittedArgument(parameter) is { } omitted)
            {
                result.Add(omitted);
            }
        }

        return (result.ToImmutable(), matched);
    }

    private static string? OmittedArgument(PassInput.Parameter parameter)
    {
        string type = Type(parameter.Type);
        if (parameter.Modifier == "params ")
        {
            return $"{Id(parameter.Name)}: ({type})[]";
        }

        return parameter.DefaultExpression switch
        {
            null => null,
            "default" => $"{Id(parameter.Name)}: default({type})",
            var expr => $"{Id(parameter.Name)}: ({type}){expr}",
        };
    }

    private string InvokeFrom(
        PassModel.Processor caller,
        PassModel.Processor callee,
        string ir,
        ImmutableArray<string> arguments)
    {
        if (!this.calls.TryGetValue(caller, out var callees))
        {
            callees = [];
            this.calls.Add(caller, callees);
        }

        callees.Add(callee);
        return Invoke(callee, ir, arguments);
    }

    private void CheckReachability(PassModel.Processor? entry)
    {
        var pending = new Stack<PassModel.Processor>(
            this.processors.Where(static processor => processor.Method is not null));
        if (entry is not null)
        {
            pending.Push(entry);
        }

        var reachable = new HashSet<PassModel.Processor>();
        while (pending.Count > 0)
        {
            var processor = pending.Pop();
            if (reachable.Add(processor) && this.calls.TryGetValue(processor, out var callees))
            {
                foreach (var callee in callees)
                {
                    pending.Push(callee);
                }
            }
        }

        foreach (var processor in this.processors)
        {
            if (processor.Method is null && !reachable.Contains(processor))
            {
                foreach (var clause in processor.Clauses)
                {
                    this.Invalid(clause.Method, $"has unreachable rewrite '{clause.Method.Name}'.");
                }
            }
        }
    }

    private PassModel.Kind? Entry(PassGrammar grammar)
    {
        var endpoint = grammar.Endpoint;
        if (grammar.Language is null && !endpoint.IsRaw)
        {
            this.Invalid(null, $"does not recognize language '{endpoint.Name}'.");
            return null;
        }

        var kind = grammar.Find(grammar.Entry);
        if (kind is null || (!endpoint.IsRaw && kind.NonTerminal is null))
        {
            this.Invalid(null, $"has unknown entry nonterminal '{grammar.Entry}'.");
            return null;
        }

        return kind;
    }

    private void ReadProcessor(PassInput.Method method, PassInput.Processor processor)
    {
        if (!ValidMethod(method) || (method.IsStatic && method.IsPartialDeclaration))
        {
            this.Invalid(method, $"has invalid processor '{method.Name}'.");
            return;
        }

        var inputType = method.Parameters[0].Type;
        var outputType = method.ReturnType.Primary;
        string parameterName = method.Parameters[0].Name;
        var from = processor.From is null
            ? this.source.Infer(inputType, parameterName)
            : this.source.Find(processor.From, inputType);
        var to = processor.To is null
            ? this.target.Infer(outputType, parameterName)
            : this.target.Find(processor.To, outputType);
        if (from is null || to is null || !Same(from.Type, inputType) || !Same(to.Type, outputType))
        {
            string message = $"has invalid processor '{method.Name}' for kinds "
                + $"'{processor.From}' and '{processor.To}'.";
            this.Invalid(method, message);
            return;
        }

        if (!processor.ValidDefaults)
        {
            this.Invalid(method, $"has invalid default extra returns on '{method.Name}'.");
            return;
        }

        this.processors.Add(new(
            method.Name,
            from,
            to,
            method.ReturnType,
            Parameters(method.Parameters.Skip(1)),
            method,
            processor.Defaults));
    }

    private bool Converts(PassType from, PassType to) =>
        Same(from, to)
        || this.target.CanConvert(from, to)
        || (from.IsTuple && to.IsTuple && from.Arguments.Count == to.Arguments.Count
            && from.Arguments.Zip(to.Arguments, this.Converts).All(static match => match))
        || input.Conversions.Contains(new(from.Key, to.Key));

    private bool ValidRunResult(PassInput.Method run, PassType targetType) =>
        run.IsPartialDeclaration
            ? !run.IsStatic && Same(run.ReturnType, targetType)
            : this.Converts(run.ReturnType, targetType);

    private PassModel.Processor? Select(
        PassModel.Kind from,
        PassModel.Kind to,
        ImmutableArray<PassInput.Parameter> ambient,
        PassInput.Method? where,
        bool synthesize,
        bool report = true,
        Func<PassModel.Processor, bool>? compatible = null)
    {
        var candidates = this.processors.Where(processor =>
            (processor.Method is not null || (!synthesize && processor.Clauses.Count > 0))
            && processor.From == from && processor.To == to
            && (compatible is null || compatible(processor)))
            .Select(processor => (Processor: processor, Binding: Bind(processor, ambient)))
            .Where(static item => item.Binding is not null)
            .OrderByDescending(static item => item.Binding!.Value.Matched)
            .Take(2).ToArray();
        if (candidates.Length > 0)
        {
            if (candidates.Length == 2
                && candidates[0].Binding!.Value.Matched == candidates[1].Binding!.Value.Matched)
            {
                if (report)
                {
                    this.Invalid(
                        where,
                        $"has ambiguous processors for '{from.Name}' to '{to.Name}'.");
                }

                return null;
            }

            return candidates[0].Processor;
        }

        return synthesize && from.NonTerminal is not null && to.NonTerminal is not null
            ? this.InferProcessor(from, to)
            : null;
    }

    private PassModel.Processor InferProcessor(PassModel.Kind from, PassModel.Kind to)
    {
        var existing = this.processors.FirstOrDefault(processor => processor.Method is null
            && processor.From == from && processor.To == to);
        if (existing is not null)
        {
            return existing;
        }

        string stem = "__NanopassVisit" + ProductionReader.Identifier(from.Name, "Input");
        string name = stem;
        for (int suffix = 2; !this.names.Add(name); suffix++)
        {
            name = stem + suffix.ToString(CultureInfo.InvariantCulture);
        }

        var inferred = new PassModel.Processor(
            name,
            from,
            to,
            to.Type,
            [],
            null,
            []);
        this.processors.Add(inferred);
        return inferred;
    }

    private void ReadRewrite(
        PassInput.Method method, PassInput.Rewrite rewrite, PassModel.Processor? entry)
    {
        var requested = this.processors.Where(processor => processor.Method is not null
            && string.Equals(processor.Name, rewrite.Processor, StringComparison.Ordinal))
            .Take(2).ToArray();
        PassModel.Processor? processor = null;
        LanguageModel.Production? rule = null;
        if (requested.Length == 1)
        {
            processor = requested[0];
        }
        else if (rewrite.Processor is null)
        {
            var ambient = entry?.Parameters ?? [];
            var owners = this.source.Kinds
                .Select(kind => (Kind: kind, Rules: MatchingRules(kind, ambient)))
                .Where(static item => item.Rules.Length > 0)
                .ToArray();
            if (owners.Any(static owner =>
                    owner.Rules.Any(static rule => rule.Alternative is null)))
            {
                owners = owners.Where(static owner =>
                    owner.Rules.Any(static rule => rule.Alternative is null)).ToArray();
            }

            var outputKind = this.target.Infer(method.ReturnType.Primary);
            bool convertsToEntry = entry is not null
                && owners.Any(item => item.Kind == entry.From)
                && this.Converts(method.ReturnType, entry.Result);
            if (rewrite.IsElse || (convertsToEntry && entry!.To == outputKind))
            {
                processor = entry;
            }
            else if (owners.Length == 1 && outputKind is { NonTerminal: not null })
            {
                processor = this.Select(
                    owners[0].Kind, outputKind, ambient, method, synthesize: !convertsToEntry)
                    ?? (convertsToEntry ? entry : null);
            }
            else if (convertsToEntry)
            {
                processor = entry;
            }
        }

        if (processor is not null && !rewrite.IsElse)
        {
            var matching = MatchingRules(processor.From, processor.Parameters);
            rule = matching.Length == 1 ? matching[0] : null;
        }

        if (processor is null || !processor.IsSynthesized || (rule is null && !rewrite.IsElse)
            || method.IsPartialDeclaration || method.TypeParameters.Count != 0
            || method.ReturnModifier.Length != 0 || method.ReturnType.Name == "void"
            || !method.Parameters.All(static parameter => IsValueParameter(parameter))
            || !this.Converts(method.ReturnType, processor.Result))
        {
            this.Invalid(method, $"has invalid rewrite '{method.Name}'.");
            return;
        }

        processor.Clauses.Add(new(method, rewrite, rule));

        LanguageModel.Production[] MatchingRules(
            PassModel.Kind kind, ImmutableArray<PassInput.Parameter> ambient)
        {
            var parameters = method.Parameters.Where(parameter => parameter.Recursion is null
                && !ambient.Any(value =>
                    value.Name == parameter.Name && Same(value.Type, parameter.Type)))
                .ToArray();
            var byType = PassGrammar.Rules(kind).Where(production =>
            {
                var type = this.source.RuleType(production);
                return rewrite.Production is not null
                    ? Same(rewrite.Production, type)
                    : parameters.Any(parameter => Same(parameter.Type, type));
            }).ToArray();
            if (byType.Length <= 1)
            {
                return byType;
            }

            var records = byType.Where(static candidate => candidate.Alternative is null).ToArray();
            if (records.Length == 1)
            {
                return records;
            }

            var named = byType.Where(candidate =>
                candidate.Alternative is { IsNonTerminal: false } field
                && parameters.Any(parameter =>
                    this.source.MatchesTerminal(field.Target, parameter.Name)
                    && Same(parameter.Type, this.source.RuleType(candidate)))).ToArray();
            var firstType = parameters.FirstOrDefault()?.Type;
            return named.Length > 0
                ? named
                : byType.Where(candidate => Same(this.source.RuleType(candidate), firstType))
                    .ToArray();
        }
    }

    private void Invalid(PassInput.Method? method, string message) =>
        this.problems.Add(new(
            3,
            method?.Location ?? input.Location,
            $"Pass '{input.Name}' {message}"));
}
