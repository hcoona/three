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
using System.Linq;

namespace Hcoona.Nanopass;

/// <summary>
/// Provides S-expression rendering, grammar diffing, and reachability pruning for
/// <see cref="LanguageDescriptor"/> definitions.
/// </summary>
public static class LanguageServices
{
    /// <summary>
    /// Renders the complete grammar, optionally retaining terminal and production prettifiers.
    /// </summary>
    /// <param name="language">The language descriptor to render.</param>
    /// <param name="includePrettifiers">
    /// <see langword="true"/> to emit <c>=&gt;</c> prettifier clauses; otherwise,
    /// <see langword="false"/>.
    /// </param>
    /// <returns>
    /// The <c>define-language</c> S-expression representing <paramref name="language"/>.
    /// </returns>
    public static SExpr ToSExpression(LanguageDescriptor language, bool includePrettifiers = false)
    {
        ArgumentNullException.ThrowIfNull(language);
        return RenderLanguage(language, includePrettifiers, omitEmptyTerminals: false);
    }

    /// <summary>
    /// Renders the grammar delta in Nanopass extension syntax.
    /// </summary>
    /// <param name="source">The base language descriptor.</param>
    /// <param name="target">The derived language descriptor.</param>
    /// <returns>
    /// A <c>define-language</c> S-expression expressing <paramref name="target"/> as an extension
    /// of <paramref name="source"/>.
    /// </returns>
    /// <remarks>
    /// Like the Racket operation, comparison uses terminal names and production syntax;
    /// changes to handlers alone are not emitted.
    /// </remarks>
    public static SExpr Diff(LanguageDescriptor source, LanguageDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        List<SExpr> clauses =
        [
            Sym("define-language"),
            Sym(target.Name),
            SExpr.List(Sym("extends"), Sym(source.Name)),
            SExpr.List(Sym("entry"), Sym(target.Entry)),
        ];

        IEnumerable<SExpr> terminals = Changes(
            WithoutNames(source.Terminals, target.Terminals, static x => x.Name)
                .Select(static x => RenderTerminal(x, includePrettifiers: false)),
            WithoutNames(target.Terminals, source.Terminals, static x => x.Name)
                .Select(static x => RenderTerminal(x, includePrettifiers: false)));
        SExpr[] terminalChanges = terminals.ToArray();
        if (terminalChanges.Length > 0)
        {
            clauses.Add(SExpr.List([Sym("terminals"), .. terminalChanges]));
        }

        var targetByName = target.NonTerminals.ToDictionary(static x => x.Name);
        Stack<SExpr> nonTerminalChanges = new();
        foreach (NonTerminalDescriptor previous in source.NonTerminals)
        {
            IEnumerable<SExpr> previousSyntax = previous.Productions.Select(static x => x.Syntax);
            if (targetByName.Remove(previous.Name, out NonTerminalDescriptor? next))
            {
                IEnumerable<SExpr> nextSyntax = next.Productions.Select(static x => x.Syntax);
                SExpr[] differences =
                [
                    .. Changes(
                        previousSyntax.Except(nextSyntax), nextSyntax.Except(previousSyntax)),
                ];
                if (differences.Length > 0)
                {
                    nonTerminalChanges.Push(RenderNonTerminal(next, differences));
                }
            }
            else
            {
                nonTerminalChanges.Push(RenderNonTerminal(
                    previous, [SExpr.List([Sym("-"), .. previousSyntax])]));
            }
        }

        foreach (NonTerminalDescriptor next in target.NonTerminals)
        {
            if (targetByName.ContainsKey(next.Name))
            {
                Seq<SExpr> productions = next.Productions.Select(static x => x.Syntax);
                SExpr added = SExpr.List([Sym("+"), .. productions]);
                nonTerminalChanges.Push(RenderNonTerminal(next, [added]));
            }
        }

        clauses.AddRange(nonTerminalChanges);
        return SExpr.List([.. clauses]);
    }

    /// <summary>
    /// Renders the grammar reachable from the entry nonterminal, without prettifiers.
    /// </summary>
    /// <param name="language">The language descriptor to prune and render.</param>
    /// <returns>The pruned <c>define-language</c> S-expression.</returns>
    public static SExpr Prune(LanguageDescriptor language) => RenderLanguage(
        PruneDescriptor(language), includePrettifiers: false, omitEmptyTerminals: true);

    /// <summary>
    /// Keeps only the nonterminals and terminals reachable from the entry,
    /// retaining their original descriptor metadata.
    /// </summary>
    /// <param name="language">The language descriptor to prune.</param>
    /// <returns>
    /// A copy of <paramref name="language"/> restricted to reachable grammar items.
    /// </returns>
    public static LanguageDescriptor PruneDescriptor(LanguageDescriptor language)
    {
        ArgumentNullException.ThrowIfNull(language);
        NonTerminalDescriptor entry =
            language.NonTerminals.FirstOrDefault(x => x.Name == language.Entry)
            ?? throw new NanopassException($"Unrecognized entry nonterminal: {language.Entry}.");

        var terminalMetaVars = IndexMetaVariables(language.Terminals, static x => x.MetaVariables);
        var nonTerminalMetaVars = IndexMetaVariables(
            language.NonTerminals, static x => x.MetaVariables);
        HashSet<Symbol> scheduled = [entry.Name];
        HashSet<Symbol> seenTerminals = [];
        Stack<NonTerminalDescriptor> pending = new();
        Stack<NonTerminalDescriptor> reachableNonTerminals = new();
        Stack<TerminalDescriptor> reachableTerminals = new();
        pending.Push(entry);

        while (pending.TryPop(out NonTerminalDescriptor? current))
        {
            reachableNonTerminals.Push(current);
            foreach (ProductionDescriptor production in current.Productions)
            {
                if (production.ReferencedTerminals is { } terminalNames
                    && production.ReferencedNonTerminals is { } nonTerminalNames)
                {
                    foreach (Symbol terminalName in terminalNames)
                    {
                        ScheduleTerminal(language.Terminals.First(
                            item => item.Name == terminalName));
                    }

                    foreach (Symbol nonTerminalName in nonTerminalNames)
                    {
                        ScheduleNonTerminal(language.NonTerminals.First(
                            item => item.Name == nonTerminalName));
                    }

                    continue;
                }

                SExpr fields = production.Syntax;
                if (fields is SExpr.Pair { Car: SExpr.Sym headSymbol } pair)
                {
                    Symbol head = RawMetaVariable(headSymbol.Value);
                    if (!terminalMetaVars.ContainsKey(head)
                        && !nonTerminalMetaVars.ContainsKey(head))
                    {
                        fields = pair.Cdr;
                    }
                }

                foreach (Symbol field in EnumerateFields(fields))
                {
                    Symbol name = RawMetaVariable(field);
                    if (terminalMetaVars.TryGetValue(name, out TerminalDescriptor? terminal))
                    {
                        ScheduleTerminal(terminal);
                    }
                    else if (nonTerminalMetaVars.TryGetValue(name, out var nonTerminal))
                    {
                        ScheduleNonTerminal(nonTerminal);
                    }
                }
            }
        }

        return language with
        {
            Terminals = [.. reachableTerminals],
            NonTerminals = [.. reachableNonTerminals],
        };

        void ScheduleTerminal(TerminalDescriptor terminal)
        {
            if (seenTerminals.Add(terminal.Name))
            {
                reachableTerminals.Push(terminal);
            }
        }

        void ScheduleNonTerminal(NonTerminalDescriptor nonTerminal)
        {
            if (scheduled.Add(nonTerminal.Name))
            {
                pending.Push(nonTerminal);
            }
        }
    }

    private static SExpr RenderLanguage(
        LanguageDescriptor language, bool includePrettifiers, bool omitEmptyTerminals)
    {
        List<SExpr> clauses =
        [
            Sym("define-language"),
            Sym(language.Name),
            SExpr.List(Sym("entry"), Sym(language.Entry)),
        ];
        if (!omitEmptyTerminals || !language.Terminals.IsEmpty)
        {
            var terminals = language.Terminals.Select(x => RenderTerminal(x, includePrettifiers));
            clauses.Add(SExpr.List([Sym("terminals"), .. terminals]));
        }

        foreach (NonTerminalDescriptor nonTerminal in language.NonTerminals)
        {
            clauses.Add(RenderNonTerminal(nonTerminal, nonTerminal.Productions.Select(
                x => IncludePrettifier(x.Syntax, x.Pretty, includePrettifiers))));
        }

        return SExpr.List([.. clauses]);
    }

    private static SExpr RenderTerminal(TerminalDescriptor terminal, bool includePrettifiers) =>
        IncludePrettifier(
            SExpr.List(Sym(terminal.Name), RenderMetaVariables(terminal.MetaVariables)),
            terminal.Prettifier,
            includePrettifiers);

    private static SExpr RenderNonTerminal(
        NonTerminalDescriptor nonTerminal, IEnumerable<SExpr> productions) =>
        SExpr.List(
            [
                Sym(nonTerminal.Name),
                RenderMetaVariables(nonTerminal.MetaVariables),
                .. productions,
            ]);

    private static SExpr RenderMetaVariables(Seq<Symbol> metaVariables) =>
        SExpr.List(metaVariables.Select<SExpr>(Sym).AsSpan());

    private static SExpr IncludePrettifier(SExpr syntax, SExpr? pretty, bool includePrettifiers) =>
        includePrettifiers && pretty is not null ? SExpr.List(Sym("=>"), syntax, pretty) : syntax;

    private static SExpr.Sym Sym(Symbol name) => new(name);

    private static IEnumerable<T> WithoutNames<T>(Seq<T> source, Seq<T> other, Func<T, Symbol> name)
    {
        HashSet<Symbol> otherNames = other.Select(name).ToHashSet();
        return source.Where(x => !otherNames.Contains(name(x)));
    }

    private static IEnumerable<SExpr> Changes(IEnumerable<SExpr> removed, IEnumerable<SExpr> added)
    {
        SExpr[] oldItems = [.. removed.Reverse()];
        if (oldItems.Length > 0)
        {
            yield return SExpr.List([Sym("-"), .. oldItems]);
        }

        SExpr[] newItems = [.. added.Reverse()];
        if (newItems.Length > 0)
        {
            yield return SExpr.List([Sym("+"), .. newItems]);
        }
    }

    private static Dictionary<Symbol, T> IndexMetaVariables<T>(
        Seq<T> source, Func<T, Seq<Symbol>> metaVariables)
    {
        Dictionary<Symbol, T> index = [];
        foreach (T item in source)
        {
            foreach (Symbol metaVariable in metaVariables(item))
            {
                index.Add(metaVariable, item);
            }
        }

        return index;
    }

    private static IEnumerable<Symbol> EnumerateFields(SExpr syntax)
    {
        Stack<SExpr> pending = new();
        pending.Push(syntax);
        while (pending.TryPop(out SExpr? next))
        {
            if (next is SExpr.Pair pair)
            {
                if (pair.Car is SExpr.Sym { Value.Name: "maybe" }
                    && pair.Cdr is SExpr.Pair { Car: SExpr.Sym field, Cdr: SExpr.Nil })
                {
                    yield return field.Value;
                }
                else
                {
                    pending.Push(pair.Cdr);
                    pending.Push(pair.Car);
                }
            }
            else if (next is SExpr.Sym symbol && symbol.Value.Name is not "...")
            {
                yield return symbol.Value;
            }
        }
    }

    private static Symbol RawMetaVariable(Symbol value)
    {
        string name = value.ToString();
        int length = name.Length;
        while (length > 0 && name[length - 1] is (>= '0' and <= '9') or '*' or '?' or '^')
        {
            length--;
        }

        return length == 0 || length == name.Length ? value : new(name[..length]);
    }
}
