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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Hcoona.Nanopass;

/// <summary>
/// Provides runtime parsing, unparsing, node counting, and term recognition infrastructure to
/// support generated Nanopass language implementations.
/// </summary>
/// <param name="name">The name of the generated language.</param>
/// <param name="terminals">The runtime terminal descriptors of the generated language.</param>
/// <param name="nonTerminals">
/// The runtime nonterminal descriptors of the generated language.
/// </param>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LanguageRuntime(
    string name,
    LanguageRuntime.Terminal[] terminals,
    LanguageRuntime.NonTerminal[] nonTerminals)
{
    private readonly Dictionary<Type, NonTerminal> unions =
        nonTerminals.ToDictionary(static nonTerminal => nonTerminal.Type);

    private readonly Dictionary<Type, Rule> records = nonTerminals
        .SelectMany(static nonTerminal => nonTerminal.Rules)
        .Where(static rule => rule.RecordType is not null)
        .GroupBy(static rule => rule.RecordType!)
        .ToDictionary(static group => group.Key, static group => group.First());

    /// <summary>
    /// Reads and validates a terminal value from an <see cref="SExpr"/> datum for generated
    /// Nanopass language implementations.
    /// </summary>
    /// <typeparam name="T">The expected CLR representation type of the terminal.</typeparam>
    /// <param name="datum">The input S-expression datum.</param>
    /// <param name="predicate">An optional terminal predicate.</param>
    /// <returns>The boxed terminal value if accepted; otherwise, <see langword="null"/>.</returns>
    public static object? ReadTerminal<T>(SExpr datum, Func<T, bool>? predicate = null)
    {
        object? value = datum is T typedDatum && typeof(T) != typeof(object)
            ? typedDatum
            : RawTerminal<T>(datum);
        return value is T typed && (predicate is null || predicate(typed)) ? typed : null;
    }

    /// <summary>
    /// Converts a captured array of elements into a <see cref="Seq{T}"/> for generated Nanopass
    /// language implementations.
    /// </summary>
    /// <typeparam name="T">The target element type.</typeparam>
    /// <param name="value">The captured <see cref="object"/> array.</param>
    /// <param name="convert">The element conversion function.</param>
    /// <returns>A <see cref="Seq{T}"/> of converted elements.</returns>
    public static Seq<T> Sequence<T>(object? value, Func<object?, T> convert) =>
        [.. ((object?[])value!).Select(convert)];

    /// <summary>
    /// Parses an <see cref="SExpr"/> datum as the specified nonterminal for generated Nanopass
    /// language implementations.
    /// </summary>
    /// <param name="nonTerminal">The zero-based nonterminal index.</param>
    /// <param name="datum">The input S-expression datum.</param>
    /// <returns>The parsed nonterminal union value.</returns>
    public object Parse(int nonTerminal, SExpr datum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        return this.TryParse(nonTerminal, datum, new(ReferenceEqualityComparer.Instance))
            ?? throw new NanopassParseException(
                $"Invalid {name} syntax for {nonTerminals[nonTerminal].Name}: {datum}.");
    }

    /// <summary>
    /// Unparses a language term into an <see cref="SExpr"/> datum for generated Nanopass language
    /// implementations.
    /// </summary>
    /// <param name="value">The term or terminal value to unparse.</param>
    /// <param name="raw">
    /// <see langword="true"/> to bypass prettifiers; <see langword="false"/> to apply them.
    /// </param>
    /// <returns>The rendered <see cref="SExpr"/> datum.</returns>
    public SExpr Unparse(object? value, bool raw) => this.Unparse(null, value, raw);

    /// <summary>
    /// Counts the AST nodes in a language term for generated Nanopass language implementations.
    /// </summary>
    /// <param name="value">The language term to inspect.</param>
    /// <returns>The total number of AST nodes in <paramref name="value"/>.</returns>
    public int CountNodes(object? value)
    {
        value = this.Unwrap(value);
        if (value is not null && this.records.TryGetValue(value.GetType(), out Rule? rule))
        {
            int count = 1;
            object?[] fields = rule.Read!(value);
            for (int i = 0; i < fields.Length; i++)
            {
                Field field = rule.Fields[i];
                if (field.IsNonTerminal)
                {
                    count += this.CountField(fields[i], field.SequenceDepth);
                }
            }

            return count;
        }

        if (value is not null && terminals.Any(terminal => terminal.Accept(value)))
        {
            return 1;
        }

        throw new NanopassException($"unrecognized {name} language record {value ?? "<null>"}.");
    }

    /// <summary>
    /// Determines whether a value belongs to any nonterminal of the generated Nanopass language.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> is a valid term of the language;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool IsTerm(object? value)
    {
        value = this.Unwrap(value);
        return value is not null
            && nonTerminals.Any(nonTerminal => this.IsTerm(nonTerminal, value, []));
    }

    /// <summary>
    /// Determines whether a value belongs to a specific nonterminal of the generated Nanopass
    /// language.
    /// </summary>
    /// <param name="nonTerminal">The zero-based nonterminal index.</param>
    /// <param name="value">The value to test.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="value"/> belongs to the specified nonterminal;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool IsTerm(int nonTerminal, object? value)
    {
        value = this.Unwrap(value);
        return value is not null && this.IsTerm(nonTerminals[nonTerminal], value, []);
    }

    private static object? RawTerminal<T>(SExpr datum) =>
        datum switch
        {
            SExpr.Bool boolean => boolean.Value,
            SExpr.Int integer when typeof(T) == typeof(int)
                && integer.Value is >= int.MinValue and <= int.MaxValue =>
                (int)integer.Value,
            SExpr.Int integer when typeof(T) == typeof(short)
                && integer.Value is >= short.MinValue and <= short.MaxValue =>
                (short)integer.Value,
            SExpr.Int integer when typeof(T) == typeof(byte)
                && integer.Value is >= byte.MinValue and <= byte.MaxValue =>
                (byte)integer.Value,
            SExpr.Int integer => integer.Value,
            SExpr.Float floating when typeof(T) == typeof(float) => (float)floating.Value,
            SExpr.Float floating => floating.Value,
            SExpr.Str text => text.Value,
            SExpr.Sym symbol => symbol.Value,
            SExpr.Char character => character.Value,
            SExpr.Boxed boxed => boxed.Value,
            _ => datum,
        };

    private bool IsTerm(NonTerminal nonTerminal, object value, HashSet<NonTerminal> active)
    {
        if (!active.Add(nonTerminal))
        {
            return false;
        }

        try
        {
            return nonTerminal.Rules.Any(rule => rule.Alternative switch
            {
                { IsNonTerminal: true } field =>
                    this.IsTerm(nonTerminals[field.Target], value, active),
                { } field => terminals[field.Target].Accept(value),
                _ => rule.RecordType == value.GetType(),
            });
        }
        finally
        {
            active.Remove(nonTerminal);
        }
    }

    private object? TryParse(int index, SExpr datum, Dictionary<SExpr, HashSet<int>> active)
    {
        if (!active.TryGetValue(datum, out HashSet<int>? indices))
        {
            indices = [];
            active.Add(datum, indices);
        }

        if (!indices.Add(index))
        {
            return null;
        }

        try
        {
            bool isPair = datum is SExpr.Pair;
            foreach (Rule rule in nonTerminals[index].Rules)
            {
                if (rule.Alternative is Field field)
                {
                    if (isPair && !field.IsNonTerminal)
                    {
                        continue;
                    }

                    object? value = field.IsNonTerminal
                        ? this.TryParse(field.Target, datum, active)
                        : terminals[field.Target].Parse(datum);
                    if (value is not null)
                    {
                        return rule.Create([value]);
                    }
                }
                else if (isPair && rule.Pattern.TryMatch(datum, ReadField, out object?[] values))
                {
                    return rule.Create(values);
                }
            }

            return null;
        }
        finally
        {
            indices.Remove(index);
            if (indices.Count == 0)
            {
                active.Remove(datum);
            }
        }

        object? ReadField(Field field, SExpr fieldDatum) => field.IsNonTerminal
            ? this.TryParse(field.Target, fieldDatum, active)
            : terminals[field.Target].Parse(fieldDatum);
    }

    private object? Unwrap(object? value)
    {
        while (value is not null && this.unions.TryGetValue(value.GetType(), out var nonTerminal))
        {
            value = nonTerminal.GetValue(value);
        }

        return value;
    }

    private SExpr Unparse(NonTerminal? nonTerminal, object? value, bool raw)
    {
        while (value is not null
            && (nonTerminal is not null
                || this.unions.TryGetValue(value.GetType(), out nonTerminal)))
        {
            value = nonTerminal.GetValue(value);
            if (value is not null && this.TryPrintAlternative(nonTerminal, value, raw) is { } datum)
            {
                return datum;
            }

            nonTerminal = null;
        }

        if (value is not null)
        {
            if (this.records.TryGetValue(value.GetType(), out Rule? rule))
            {
                return this.Render(rule, value, raw);
            }

            foreach (Terminal terminal in terminals)
            {
                if (terminal.Accept(value))
                {
                    return raw ? SExpr.FromValue(value) : terminal.Print(value);
                }
            }
        }

        throw new NanopassException($"unrecognized {name} language form {value ?? "<null>"}.");
    }

    private SExpr? TryPrintAlternative(NonTerminal nonTerminal, object value, bool raw)
    {
        var type = value.GetType();
        bool isLeaf = !this.unions.ContainsKey(type) && !this.records.ContainsKey(type);
        foreach (var rule in nonTerminal.Rules)
        {
            if (rule.Alternative is not { } field)
            {
                continue;
            }

            if (!field.IsNonTerminal)
            {
                if (isLeaf && terminals[field.Target].Accept(value))
                {
                    return rule.Pattern.Render([value], raw, (item, leaf) =>
                        this.PrintField(item, leaf, raw));
                }

                continue;
            }

            if (raw || !rule.HasPrettyTemplate)
            {
                continue;
            }

            var target = nonTerminals[field.Target];
            if (target.Type == type && this.Unwrap(value) is { } form
                && this.IsTerm(target, form, []))
            {
                var inner = this.Unparse(target, value, raw);
                return rule.Pattern.Render([value], raw, (_, _) => inner);
            }
        }

        return null;
    }

    private SExpr Render(Rule rule, object value, bool raw)
    {
        var fields = rule.Read!(value);
        SExpr PrintField(Field field, object leaf) => this.PrintField(field, leaf, raw);

        return !raw && rule.PrettyHandler is { } handler
            ? handler(rule.Pattern.Render(fields, raw: true, PrintField))
            : rule.Pattern.Render(fields, raw, PrintField);
    }

    private SExpr PrintField(Field field, object value, bool raw) => field.IsNonTerminal
        ? this.Unparse(value, raw)
        : raw ? SExpr.FromValue(value) : terminals[field.Target].Print(value);

    private int CountField(object? value, int depth)
    {
        if (value is null)
        {
            return 0;
        }

        if (depth == 0)
        {
            return this.CountNodes(value);
        }

        return ((IEnumerable)value).Cast<object?>().Sum(item => this.CountField(item, depth - 1));
    }

    /// <summary>
    /// Describes a runtime terminal handler table entry supporting generated Nanopass language
    /// implementations.
    /// </summary>
    /// <param name="Parse">Reads a terminal value from an <see cref="SExpr"/> datum.</param>
    /// <param name="Accept">Tests whether a runtime CLR object satisfies the terminal.</param>
    /// <param name="Print">Renders a terminal value as a prettified <see cref="SExpr"/>.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed record Terminal(
        Func<SExpr, object?> Parse,
        Func<object, bool> Accept,
        Func<object, SExpr> Print);

    /// <summary>
    /// Describes a runtime nonterminal dispatch entry supporting generated Nanopass language
    /// implementations.
    /// </summary>
    /// <param name="Name">The grammar name of the nonterminal.</param>
    /// <param name="Type">The generated union CLR type of the nonterminal.</param>
    /// <param name="GetValue">Unwraps the underlying case value from a union instance.</param>
    /// <param name="Rules">The ordered production dispatch rules of the nonterminal.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed record NonTerminal(
        string Name,
        Type Type,
        Func<object, object?> GetValue,
        Rule[] Rules);

    /// <summary>
    /// Describes a captured metavariable field in a runtime production rule supporting generated
    /// Nanopass language implementations.
    /// </summary>
    /// <param name="Name">The metavariable name in the pattern.</param>
    /// <param name="Target">The zero-based target nonterminal or terminal index.</param>
    /// <param name="IsNonTerminal">
    /// <see langword="true"/> if <paramref name="Target"/> indexes nonterminals;
    /// <see langword="false"/> if it indexes terminals.
    /// </param>
    /// <param name="SequenceDepth">The ellipsis nesting depth of the field.</param>
    /// <param name="IsMaybe">
    /// <see langword="true"/> if the field accepts <c>#f</c> as <see langword="null"/>; otherwise,
    /// <see langword="false"/>.
    /// </param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed record Field(
        string Name, int Target, bool IsNonTerminal, int SequenceDepth, bool IsMaybe);

    /// <summary>
    /// Describes a runtime production pattern and construction rule supporting generated Nanopass
    /// language implementations.
    /// </summary>
    /// <param name="syntax">The raw S-expression pattern string.</param>
    /// <param name="pretty">The optional S-expression prettifier template string.</param>
    /// <param name="alternative">
    /// The standalone terminal or nonterminal field when the rule has no record wrapper.
    /// </param>
    /// <param name="fields">The ordered record fields captured by the rule.</param>
    /// <param name="type">The generated production record CLR type, if any.</param>
    /// <param name="create">
    /// Constructs the union or record instance from captured field values.
    /// </param>
    /// <param name="read">Extracts field values from a record instance for unparsing.</param>
    /// <param name="prettyHandler">An optional custom prettifier callback.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Rule(
        string syntax,
        string? pretty,
        Field? alternative,
        Field[] fields,
        Type? type,
        Func<object?[], object> create,
        Func<object, object?[]>? read,
        Func<SExpr, SExpr>? prettyHandler = null)
    {
        internal GrammarPattern Pattern { get; } =
            new(syntax, pretty, alternative is null ? fields : [alternative]);

        internal bool HasPrettyTemplate { get; } = pretty is not null;

        internal Field? Alternative { get; } = alternative;

        internal Field[] Fields { get; } = fields;

        internal Type? RecordType { get; } = type;

        internal Func<object?[], object> Create { get; } = create;

        internal Func<object, object?[]>? Read { get; } = read;

        internal Func<SExpr, SExpr>? PrettyHandler { get; } = prettyHandler;
    }
}
