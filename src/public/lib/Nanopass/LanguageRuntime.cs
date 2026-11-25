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

    public static object? ReadTerminal<T>(SExpr datum, Func<T, bool>? predicate = null)
    {
        object? value = datum is T typedDatum && typeof(T) != typeof(object)
            ? typedDatum
            : RawTerminal<T>(datum);
        return value is T typed && (predicate is null || predicate(typed)) ? typed : null;
    }

    public static Seq<T> Sequence<T>(object? value, Func<object?, T> convert) =>
        [.. ((object?[])value!).Select(convert)];

    public object Parse(int nonTerminal, SExpr datum)
    {
        ArgumentNullException.ThrowIfNull(datum);
        return this.TryParse(nonTerminal, datum, new(ReferenceEqualityComparer.Instance))
            ?? throw new NanopassParseException(
                $"Invalid {name} syntax for {nonTerminals[nonTerminal].Name}: {datum}.");
    }

    public SExpr Unparse(object? value, bool raw)
    {
        while (value is not null && this.unions.TryGetValue(value.GetType(), out var nonTerminal))
        {
            value = nonTerminal.GetValue(value);
            if (value is not null && this.TryPrintAlternative(nonTerminal, value, raw) is { } datum)
            {
                return datum;
            }
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

    public bool IsTerm(object? value) =>
        value is not null && nonTerminals.Any(nonTerminal => this.IsTerm(nonTerminal, value, []));

    public bool IsTerm(int nonTerminal, object? value) =>
        value is not null && this.IsTerm(nonTerminals[nonTerminal], value, []);

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
        if (value.GetType() == nonTerminal.Type)
        {
            return nonTerminal.GetValue(value) is not null;
        }

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

    private SExpr? TryPrintAlternative(NonTerminal nonTerminal, object value, bool raw)
    {
        if (this.unions.ContainsKey(value.GetType()) || this.records.ContainsKey(value.GetType()))
        {
            return null;
        }

        foreach (var rule in nonTerminal.Rules)
        {
            if (rule.Alternative is { IsNonTerminal: false } field
                && terminals[field.Target].Accept(value))
            {
                return rule.Pattern.Render([value], raw, (item, leaf) =>
                    this.PrintField(item, leaf, raw));
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

    public sealed record Terminal(
        Func<SExpr, object?> Parse,
        Func<object, bool> Accept,
        Func<object, SExpr> Print);

    public sealed record NonTerminal(
        string Name,
        Type Type,
        Func<object, object?> GetValue,
        Rule[] Rules);

    public sealed record Field(
        string Name, int Target, bool IsNonTerminal, int SequenceDepth, bool IsMaybe);

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

        internal Field? Alternative { get; } = alternative;

        internal Field[] Fields { get; } = fields;

        internal Type? RecordType { get; } = type;

        internal Func<object?[], object> Create { get; } = create;

        internal Func<object, object?[]>? Read { get; } = read;

        internal Func<SExpr, SExpr>? PrettyHandler { get; } = prettyHandler;
    }
}
